using System;
using System.Diagnostics;

namespace σκοπός {
  internal class RuntimeMetrics {
    public RuntimeMetrics() { }
    public int num_fixed_update_iterations_ = 0;
    public double fixed_update_runtime_ = 0;

    public double AverageFixedUpdateRuntime => fixed_update_runtime_ / num_fixed_update_iterations_;
  }

  internal class FixedUpdateMetric {
    public FixedUpdateMetric(string name) { 
      this.name = name;
      watch_ = new Stopwatch();  
    }

    public void StartFixedUpdate() {
      if (calls_this_fixedupdate > 0) fixedupdate_count_++;
      ticks_start_last_fixedupdate_ = watch_.ElapsedTicks;
      calls_this_fixedupdate = 0;
    }

    public void Start() {
      total_calls++;
      calls_this_fixedupdate++;
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
    public long calls_this_fixedupdate = 0;
    public long successes = 0;
    public long failures = 0;
    Stopwatch watch_;
    public string name;
    long ticks_start_last_fixedupdate_ = 0;
    long fixedupdate_count_ = 0;
    public double average_calls_per_fixedupdate => (double) total_calls / fixedupdate_count_;
    public double average_runtime_per_fixedupdate => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / fixedupdate_count_;
    public double average_runtime_per_call => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / total_calls;
    public double total_runtime_this_fixedupdate => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_fixedupdate_).TotalSeconds;
    public double average_runtime_this_fixedupdate => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_fixedupdate_).TotalSeconds / calls_this_fixedupdate;
  }
}
