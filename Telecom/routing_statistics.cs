using System.Collections.Generic;
using System.Linq;

namespace σκοπός {
internal class RoutingStatistics : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public RoutingStatistics(Telecom telecom) : base(telecom){
    telecom_ = telecom;
  }

  protected override string Title => "Σκοπός Telecom routing statistics";

  public static string short_time_to_string(double time) {
    if (time >= 1) {
      return $"{time:F3} s";
    } else if (time >= 0.000001) {
      return $"{time * 1000:F3} ms";
    } else {
      return $"{time * 1000000:F3} μs";
    }
  }

  protected override void RenderWindowContents(int window_id) {
    if (!telecom_.enabled || telecom_.network == null) {
      UnityEngine.GUILayout.Label("Please wait for the Σκοπός Telecom network to initialize...");
      return;
    }
    if (stopwatch_metrics_ == null) {
      stopwatch_metrics_ = new List<StopwatchMetric> { 
        telecom_.network.refresh_metric,
        telecom_.network.routing_.find_channels_duplex_metric,
        telecom_.network.routing_.find_channels_ptmp_metric,
        telecom_.network.routing_.reset_metric, 
        telecom_.network.routing_.heuristic.apsp_metric,
        telecom_.network.routing_.one_hop_metric,
        telecom_.network.routing_.shortest_path_metric,
        telecom_.network.routing_.a_star_metric,
        telecom_.network.routing_.dijkstras_metric,
        telecom_.network.routing_.find_channels_metric,
        Routing.link_usage_metric,
        Routing.fake_usage_metric,
        telecom_.network.kerbalism_consumption_metric,
        Service.service_availability_metric,
      };
    }
    if (count_metrics_ == null) {
      count_metrics_ = new List<Metric> {
        Routing.OrientedLink.open_link_metric,
        Routing.OrientedLink.cache_hit_metric,
      };
    }

    var default_style = principia.ksp_plugin_adapter.Style.DarkToggleButton();
    var selected_style = principia.ksp_plugin_adapter.Style.LitToggleButton();

    using (new UnityEngine.GUILayout.HorizontalScope()) {
      var classic_tooltip = new UnityEngine.GUIContent("Default", 
        "The same routing algorithm as stable Skopos. Each connection chooses the route with minimum latency that satisties the minimum data rate, using Dijkstra's algorithm.");
      if (UnityEngine.GUILayout.Button(classic_tooltip, telecom_.routing_method_ == Routing.RoutingMethod.DIJKSTRAS ? selected_style : default_style)) {
        telecom_.routing_method_ = Routing.RoutingMethod.DIJKSTRAS;
      }
      var onehop_tooltip = new UnityEngine.GUIContent("One-Hop", 
        "For simplex/duplex connections, instead use the minimum-latency connection that only uses one vessel (\"one hop\") to route the connection, if possible. May yield different results!");
      if (UnityEngine.GUILayout.Button(onehop_tooltip, telecom_.routing_method_ == Routing.RoutingMethod.ONEHOP ? selected_style : default_style)) {
        telecom_.routing_method_ = Routing.RoutingMethod.ONEHOP;
      }
      var astar_tooltip = new UnityEngine.GUIContent("FW + A*", 
        "Precompute the best possible shortest path between all vessels/ground stations, and use that to speed up Dijkstra's algorithm for simplex/duplex connections.\n\nRequires some precomputation, which worsens the more vessels there are. This should yield identical results.");
      if (UnityEngine.GUILayout.Button(astar_tooltip, telecom_.routing_method_ == Routing.RoutingMethod.ASTAR ? selected_style : default_style)) {
        telecom_.routing_method_ = Routing.RoutingMethod.ASTAR;
      }
    }

    using (new UnityEngine.GUILayout.HorizontalScope()) { // the most bootleg table imaginable
      
      List<StopwatchMetric> filtered_metrics = stopwatch_metrics_.Where(metric => metric.calls_this_refresh > 0).ToList();
      using (new UnityEngine.GUILayout.VerticalScope()) {
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          UnityEngine.GUILayout.FlexibleSpace();
          UnityEngine.GUILayout.Label("Timing Statistics");
        }
        foreach (StopwatchMetric metric in filtered_metrics) {
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.FlexibleSpace();
            UnityEngine.GUILayout.Label(metric.name);
          }
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Total Calls");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label(metric.total_calls.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✓ Calls");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label(metric.successes.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✗ Calls", principia.ksp_plugin_adapter.Style.Error(UnityEngine.GUI.skin.label));
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label(metric.failures.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Calls");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label($"{metric.average_calls_per_refresh:F3}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Calls This Frame");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label($"{metric.calls_this_refresh}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time/Call");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.average_runtime_per_call)}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.average_runtime_post_hysteresis)}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Last Time Total");
        foreach (StopwatchMetric metric in filtered_metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.total_runtime_this_refresh)}");
        }
      }
    }
    using (new UnityEngine.GUILayout.HorizontalScope()) {
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Miscellaneous Metrics");
        foreach (Metric metric in count_metrics_) {
          UnityEngine.GUILayout.FlexibleSpace();
          UnityEngine.GUILayout.Label($"{metric.name}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Count");
        foreach (Metric metric in count_metrics_) {
          UnityEngine.GUILayout.Label($"{metric.observe_count}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Total");
        foreach (Metric metric in count_metrics_) {
          UnityEngine.GUILayout.Label($"{metric.total}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Average");
        foreach (Metric metric in count_metrics_) {
          UnityEngine.GUILayout.Label($"{metric.average_hysteresis}");
        }
      }
    }
    UnityEngine.GUI.DragWindow();
  }

  public void RenderButton() {
    if (UnityEngine.GUILayout.Button(new UnityEngine.GUIContent("Runtime Breakdown", "Runtime profiling for various segments of the routing algorithm. For use if you run into weird performance issues."))) {
      Toggle();
    }
  }

  private Telecom telecom_;
  private List<StopwatchMetric> stopwatch_metrics_;
  private List<Metric> count_metrics_;
}
}
