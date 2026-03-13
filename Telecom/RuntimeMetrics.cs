using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace σκοπός {

  internal class PerRefreshMetric {
    public PerRefreshMetric(string name) { 
      this.name = name;
      watch_ = new Stopwatch();  
    }

    public void StartRefresh() {
      if (calls_this_fixedupdate > 0) {
        fixedupdate_count_++;
        last_runs.Enqueue(total_runtime_this_refresh);
        while (last_runs.Count > 100) {
          last_runs.Dequeue();
        }
      }
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
    private Queue<double> last_runs = new Queue<double>();
    Stopwatch watch_;
    public string name;
    long ticks_start_last_fixedupdate_ = 0;
    long fixedupdate_count_ = 0;
    public double average_calls_per_refresh => (double) total_calls / fixedupdate_count_;
    public double average_runtime_per_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / fixedupdate_count_;
    public double average_runtime_per_call => (double) TimeSpan.FromTicks(watch_.ElapsedTicks).TotalSeconds / total_calls;
    public double total_runtime_this_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_fixedupdate_).TotalSeconds;
    public double average_runtime_this_refresh => (double) TimeSpan.FromTicks(watch_.ElapsedTicks - ticks_start_last_fixedupdate_).TotalSeconds / calls_this_fixedupdate;
    public double average_runtime_last_100_refreshes => (double) last_runs.Sum() / last_runs.Count;
  }
}
