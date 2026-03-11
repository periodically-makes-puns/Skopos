using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RealAntennas;

namespace σκοπός {
internal class RoutingStatistics : principia.ksp_plugin_adapter.SupervisedWindowRenderer {
  public RoutingStatistics(Telecom telecom) : base(telecom){
    Hide();
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
    using (new UnityEngine.GUILayout.HorizontalScope()) { // the most bootleg table imaginable
      FixedUpdateMetric[] metrics = { 
        telecom_.network.routing_.find_channels_duplex_metric,
        telecom_.network.routing_.find_channels_ptmp_metric,
        telecom_.network.routing_.reset_metric, 
        telecom_.network.routing_.heuristic.apsp_metric, 
        telecom_.network.routing_.one_hop_metric,
        telecom_.network.routing_.shortest_path_metric,
        telecom_.network.routing_.a_star_metric,
        telecom_.network.routing_.power_efficient_metric,
        telecom_.network.routing_.dijkstras_metric,
        telecom_.network.routing_.find_channels_metric,
        Routing.link_usage_metric,
        Routing.fake_usage_metric,
        telecom_.network.kerbalism_consumption_metric,
        Service.service_availability_metric,
      };
      using (new UnityEngine.GUILayout.VerticalScope()) {
        using (new UnityEngine.GUILayout.HorizontalScope()) {
          UnityEngine.GUILayout.FlexibleSpace();
          UnityEngine.GUILayout.Label("Timing Statistics");
        }
        foreach (FixedUpdateMetric metric in metrics) {
          using (new UnityEngine.GUILayout.HorizontalScope()) {
            UnityEngine.GUILayout.FlexibleSpace();
            UnityEngine.GUILayout.Label(metric.name);
          }
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Total Calls");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.total_calls.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✓ Calls");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.successes.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("✗ Calls", principia.ksp_plugin_adapter.Style.Error(UnityEngine.GUI.skin.label));
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label(metric.failures.ToString());
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Calls");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{metric.average_calls_per_fixedupdate:F3}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Calls This Frame");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{metric.calls_this_fixedupdate}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time/Call");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.average_runtime_per_call)}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Avg. Time Total");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.average_runtime_per_fixedupdate)}");
        }
      }
      using (new UnityEngine.GUILayout.VerticalScope()) {
        UnityEngine.GUILayout.Label("Last Time Total");
        foreach (FixedUpdateMetric metric in metrics) {
          UnityEngine.GUILayout.Label($"{short_time_to_string(metric.total_runtime_this_fixedupdate)}");
        }
      }
    }
    
    UnityEngine.GUI.DragWindow();
  }

  public void RenderButton() {
    if (UnityEngine.GUILayout.Button("Runtime Details…")) {
      Toggle();
    }
  }

  private Telecom telecom_;
}
}
