using System.Collections.Generic;
using System.Linq;
using RealAntennas;

namespace σκοπός {

internal class MainWindow : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public MainWindow(Telecom telecom) : base(telecom) {
    telecom_ = telecom;
    stats_ = new RoutingStatistics(telecom);
    vessel_overview_ = new VesselOverview(telecom);
  }

  public bool show_network { get; private set; } = false;

  protected override string Title => "Σκοπός Telecom network overview";

  protected override void RenderWindowContents(int window_id) {
    if (!telecom_.enabled || telecom_.network == null) {
      UnityEngine.GUILayout.Label("Please wait for the Σκοπός Telecom network to initialize...");
      return;
    }
    if (string.IsNullOrEmpty(alert_rate_limit_text)) {
      alert_rate_limit_text = telecom_.max_alert_rate_in_days_.ToString();
    }   // MainWindow initialization is before this field was loaded by the scenario.

    if (string.IsNullOrEmpty(cleanup_days_text)) {
      cleanup_days_text = telecom_.contract_cleanup_days_.ToString();
    } // same with this one
        
     

    using (new UnityEngine.GUILayout.VerticalScope()) {
      using (new UnityEngine.GUILayout.HorizontalScope()) {
        show_network = UnityEngine.GUILayout.Toggle(show_network, "Show network");
        telecom_.stop_warp_in_sim_ = UnityEngine.GUILayout.Toggle(telecom_.stop_warp_in_sim_, "Alerts stop warp in RP-1 sim");
        vessel_overview_.RenderButton();
        stats_.RenderButton();
      }
      using (new UnityEngine.GUILayout.HorizontalScope()) {
        UnityEngine.GUILayout.Label("Suppress duplicate SLA alerts within");
        alert_rate_limit_text = UnityEngine.GUILayout.TextField(alert_rate_limit_text);
        double.TryParse(alert_rate_limit_text, out telecom_.max_alert_rate_in_days_);
        UnityEngine.GUILayout.Label($"days ({telecom_.max_alert_rate_in_days_})");
      }

      using (new UnityEngine.GUILayout.HorizontalScope()) {
        UnityEngine.GUILayout.Label("Clean up (on scene change) maintenance contracts completed at least");
        cleanup_days_text = UnityEngine.GUILayout.TextField(cleanup_days_text);
        double.TryParse(cleanup_days_text, out telecom_.contract_cleanup_days_);
        UnityEngine.GUILayout.Label($"days ago ({telecom_.contract_cleanup_days_})");
      }

      using (new UnityEngine.GUILayout.VerticalScope()) {
        var classic_tooltip = new UnityEngine.GUIContent("Default routing", "The same routing algorithm as stable Skopos.");
        if (UnityEngine.GUILayout.Toggle(telecom_.routing_method_ == Routing.RoutingMethod.DIJKSTRAS, classic_tooltip)) {
          telecom_.routing_method_ = Routing.RoutingMethod.DIJKSTRAS;
        }
        var onehop_tooltip = new UnityEngine.GUIContent("[EXPERIMENTAL] Prefer one-bounce connections", "For simplex/duplex connections, use the minimum-latency connection that only uses one vessel to route the connection, if possible. May yield different results!");
        if (UnityEngine.GUILayout.Toggle(telecom_.routing_method_ == Routing.RoutingMethod.ONEHOP, onehop_tooltip)) {
          telecom_.routing_method_ = Routing.RoutingMethod.ONEHOP;
        }
        var astar_tooltip = new UnityEngine.GUIContent("[EXPERIMENTAL] Use A* search", "Requires some precomputation, which can be slow with many vessels. This should yield the same results, but faster.");
        if (UnityEngine.GUILayout.Toggle(telecom_.routing_method_ == Routing.RoutingMethod.ASTAR, astar_tooltip)) {
          telecom_.routing_method_ = Routing.RoutingMethod.ASTAR;
        }
        var vgv_tooltip = new UnityEngine.GUIContent("[EXPERIMENTAL] Use VGV routing", "Requires more precomputation, and primarily speeds up broadcast routing compared to A*. This also should yield the same results, but faster.");
        if (UnityEngine.GUILayout.Toggle(telecom_.routing_method_ == Routing.RoutingMethod.VGV, vgv_tooltip)) {
          telecom_.routing_method_ = Routing.RoutingMethod.VGV;
        }
        telecom_.network.routing_.method = telecom_.routing_method_;
      }

      using (new UnityEngine.GUILayout.HorizontalScope()) {
        UnityEngine.GUILayout.Label($"Contracted connections: {telecom_.network.contracted_connections.Count}");
        UnityEngine.GUILayout.Label($"Total Runs: {telecom_.network.refresh_metric.total_calls}");
        UnityEngine.GUILayout.Label($"Average Runtime (last 100): {RoutingStatistics.short_time_to_string(telecom_.network.refresh_metric.average_runtime_last_100_refreshes)}");
      }

      //using (new UnityEngine.GUILayout.VerticalScope()) {
      //  UnityEngine.GUILayout.Label($"Considered links: {Routing.link_stats.considered}");
      //  UnityEngine.GUILayout.Label($"After Dijkstra filter: {Routing.link_stats.filter1}");
      //  UnityEngine.GUILayout.Label($"After maxdatarate filter: {Routing.link_stats.filter2}");
      //  UnityEngine.GUILayout.Label($"After distance filter: {Routing.link_stats.filter3}");
      //  UnityEngine.GUILayout.Label($"After Rx bandwidth filter: {Routing.link_stats.filter4}");
      //  UnityEngine.GUILayout.Label($"After Tx power filter: {Routing.link_stats.filter5}");
      //  UnityEngine.GUILayout.Label($"After Tx bandwidth filter (taken): {Routing.link_stats.taken}");
      //  UnityEngine.GUILayout.Label($"Maximum priority queue size: {Routing.link_stats.max_pq_size}");
      //}

      var inspected_connections = connection_inspectors_.Keys.ToArray();
      foreach (var inspected_connection in inspected_connections) {
        if (!telecom_.network.contracted_connections.Contains(inspected_connection)) {
          connection_inspectors_[inspected_connection].DisposeWindow();
          connection_inspectors_.Remove(inspected_connection);
        }
      }
      foreach (var contracted_connection in telecom_.network.contracted_connections) {
       if (!connection_inspectors_.ContainsKey(contracted_connection)) {
          connection_inspectors_.Add(
              contracted_connection,
              new ConnectionInspector(telecom_, contracted_connection));
       }
      }
      foreach (var contract in open_contracts_.Keys.ToArray()) {
        if (!telecom_.network.connections_by_contract.ContainsKey(contract)) {
          open_contracts_.Remove(contract);
        }
      }
      foreach (var contract in telecom_.network.connections_by_contract.Keys) {
        if (!open_contracts_.ContainsKey(contract)) {
          open_contracts_.Add(contract, false);
        }
      }
      var ok_style = UnityEngine.GUI.skin.label;
      var disconnected_style = principia.ksp_plugin_adapter.Style.Warning(ok_style);

      foreach (var contract_connections in telecom_.network.connections_by_contract) {
        var contract = contract_connections.Key;
        var connections = contract_connections.Value;
        bool all_available = connections.All(connection => {
          if (connection is PointToMultipointConnection point_to_multipoint) {
            return point_to_multipoint.channel_services.All(service => service.basic.available);
          } else if (connection is DuplexConnection duplex) {
            return duplex.basic_service.available;
          } else { return false; }
        });
        var contract_style = all_available ? ok_style : disconnected_style;
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          if (UnityEngine.GUILayout.Button(
                open_contracts_[contract] ? "−" : "+", GUILayoutWidth(1))) {
            open_contracts_[contract] = !open_contracts_[contract];
            ScheduleShrink();
            return;
          }
          UnityEngine.GUILayout.Label(contract.Title, contract_style);
        }
        if (open_contracts_[contract]) {
          foreach (var connection in connections) {
            if (connection is PointToMultipointConnection point_to_multipoint) {
              var tx = telecom_.network.GetStation(point_to_multipoint.tx_name);
              if (point_to_multipoint.channel_services.Length == 1) {
                var services = point_to_multipoint.channel_services[0];
                var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[0]);
                bool available = services.basic.available;
                string status = available ? "OK" : "Disconnected";
                var style = available ? ok_style : disconnected_style;
                using (new UnityEngine.GUILayout.HorizontalScope()) {
                  UnityEngine.GUILayout.Label(
                      $"From {tx.displaynodeName} to {rx.displaynodeName}: {status}",
                      style,
                      GUILayoutWidth(15));
                  connection_inspectors_[connection].RenderButton();
                }
              } else {
                using (new UnityEngine.GUILayout.HorizontalScope()) {
                  UnityEngine.GUILayout.Label(
                      $"Broadcast from {tx.displaynodeName} to:",
                      GUILayoutWidth(15));
                  connection_inspectors_[connection].RenderButton();
                }
              }
              for (int i = 0; i < point_to_multipoint.rx_names.Length; ++i) {
                var services = point_to_multipoint.channel_services[i];
                bool available = services.basic.available;
                string status = available ? "OK" : "Disconnected";
                var style = available ? ok_style : disconnected_style;
                var rx = telecom_.network.GetStation(point_to_multipoint.rx_names[i]);
                if (point_to_multipoint.rx_names.Length > 1) {
                  UnityEngine.GUILayout.Label(
                    $@"— {rx.displaynodeName}: {status}", style);
                }
              }
            } else if (connection is DuplexConnection duplex) {
              var trx0 = telecom_.network.GetStation(duplex.trx_names[0]);
              var trx1 = telecom_.network.GetStation(duplex.trx_names[1]);
              bool available = duplex.basic_service.available;
              string status = available ? "OK" : "Disconnected";
              var style = available ? ok_style : disconnected_style;
              using (new UnityEngine.GUILayout.HorizontalScope()) {
                UnityEngine.GUILayout.Label(
                    $@"Duplex between {trx0.displaynodeName} and {trx1.displaynodeName}: {status}",
                    style,
                    GUILayoutWidth(15));
                connection_inspectors_[connection].RenderButton();
              }
            }
          }
        }
      }
      telecom_.network.hide_off_network = show_network;
    }
    UnityEngine.GUI.DragWindow();
  }

  public Dictionary<RealAntennaDigital, AntennaInspector> antenna_inspectors =>
    antenna_inspectors_;

  private Telecom telecom_;
  private RoutingStatistics stats_;
  private VesselOverview vessel_overview_;
  private string alert_rate_limit_text;
  private string cleanup_days_text;
  private readonly Dictionary<Contracts.Contract, bool> open_contracts_ =
      new Dictionary<Contracts.Contract, bool>();
  private readonly Dictionary<Connection, ConnectionInspector> connection_inspectors_ =
      new Dictionary<Connection, ConnectionInspector>();
  private readonly Dictionary<RealAntennaDigital, AntennaInspector> antenna_inspectors_ =
      new Dictionary<RealAntennaDigital, AntennaInspector>();
}

}
