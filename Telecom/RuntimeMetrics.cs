using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace σκοπός {

  internal class PerRefreshMetric {
    public PerRefreshMetric(string name, int hysteresis_factor = 20) { 
      this.name = name;
      watch_ = new Stopwatch();  
      hysteresis_factor_ = hysteresis_factor;
    }

    public void StartRefresh() {
      if (calls_this_refresh > 0) {
        refresh_count_++;
        int hysteresis_factor = (refresh_count_ > hysteresis_factor_) ? hysteresis_factor_ : refresh_count_;
        average_runtime_post_hysteresis = (average_runtime_post_hysteresis * hysteresis_factor + total_runtime_this_refresh) / (hysteresis_factor + 1);
      }
      ticks_start_last_refresh_ = watch_.ElapsedTicks;
      calls_this_refresh = 0;
    }

    public void Start() {
      total_calls++;
      calls_this_refresh++;
      watch_.Start();
    }

    public void Pause() {
      watch_.Stop();
    }

    public void Resume() {
      watch_.Start();
    }

    public void StopSuccess() {
      watch_.Stop();
      successes++;
    }
    public void StopFailure() {
      watch_.Stop();
      failures++;
    }

    public long total_calls = 0;
    public long calls_this_refresh = 0;
    public long successes = 0;
    public long failures = 0;
    public string name;
    private Stopwatch watch_;
    private long ticks_start_last_refresh_ = 0;
    private int refresh_count_ = 0;
    private int hysteresis_factor_ = 20;
    public double average_runtime_post_hysteresis { get; private set; } = 0;
    public double average_calls_per_refresh => (double) total_calls / refresh_count_;
    public double average_runtime_per_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / refresh_count_;
    public double average_runtime_per_call => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / total_calls;
    public double total_runtime_this_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_refresh_).TotalSeconds;
    public double average_runtime_this_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_refresh_).TotalSeconds / calls_this_refresh;
  }
}
