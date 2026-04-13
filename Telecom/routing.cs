using System;
using System.Collections.Generic;
using System.Linq;
using RealAntennas;
using static σκοπός.Routing.PointToMultipointAvailability;

namespace σκοπός {

  public class Routing {

  public enum PointToMultipointAvailability {
    Unavailable,
    Partial,
    Available,
  }

  public class Channel {
    public readonly List<OrientedLink> links = new List<OrientedLink>();
    public double latency;
  }

  public struct SourcedLink {
    public SourcedLink(Connection connection,
                       Channel channel,
                       OrientedLink link) {
      this.connection = connection;
      this.channel = channel;
      this.link = link;
    }
    public readonly Connection connection;
    public readonly Channel channel;
    public readonly OrientedLink link;
  }

  public class Circuit {
    public readonly Channel forward;
    public readonly Channel backward;

    public Circuit(Channel forward, Channel backward) {
      this.forward = forward;
      this.backward = backward;
    }

      public double round_trip_latency => forward.latency + backward.latency;
    }

  public class NetworkUsage {
    public class PowerBreakdown {
      public struct SingleUsage {
        public SourcedLink link;
        public double power ;
      }

      private double power_ = 0;
      private double fake_power_ = 0;
      public double power => power_ + fake_power_;

      public void AddUsages(SingleUsage[] broadcast, bool fake = false) {
        var delta = (from usage in broadcast select usage.power).Max();
        if (!fake) {
          power_ += delta;
          usages_.Add(broadcast);
        } else {
          fake_power_ += delta;
        }
      }

      public void ResetFakeChanges() {
        fake_power_ = 0;
      }

      public PowerBreakdown Clone() {
        return new PowerBreakdown{
          usages_ = usages.Select(usages => usages.ToArray()).ToList(),
          power_ = power_,
          fake_power_ = fake_power_
        };
      }

      public IEnumerable<SingleUsage[]> usages => usages_;

      private List<SingleUsage[]> usages_ = new List<SingleUsage[]>();
    }

    public class SpectrumBreakdown {
      public struct SingleUsage {
        public enum Kind { Transmit, Receive }
        public SourcedLink link;
        public Kind kind;
        public double spectrum ;
      }

      private double spectrum_ = 0;
      private double fake_spectrum_ = 0;
      public double spectrum => spectrum_ + fake_spectrum_;


      public void AddUsages(SingleUsage[] usage, bool fake = false) {
        if (!fake) {
          spectrum_ += usage[0].spectrum;
          usages_.Add(usage);
        } else {
          fake_spectrum_ += usage[0].spectrum;
        }
      }

      public void ResetFakeChanges() {
        fake_spectrum_ = 0;
      }

      public SpectrumBreakdown Clone() {
        return new SpectrumBreakdown{
          usages_ = usages.Select(usages => usages.ToArray()).ToList(),
          spectrum_ = spectrum_,
          fake_spectrum_ = fake_spectrum_
        };
      }

      public IEnumerable<SingleUsage[]> usages => usages_;

      private List<SingleUsage[]> usages_ = new List<SingleUsage[]>();
    }

    public static NetworkUsage None = new NetworkUsage();

    // Normalized on [0, 1];
    public double TxPowerUsage(RealAntennaDigital tx) {
      return SourcedTxPowerUsage(tx).power;
    }

    // In Hz.
    public double SpectrumUsage(RealAntennaDigital trx) {
      return SourcedSpectrumUsage(trx).spectrum;
    }

    public virtual PowerBreakdown SourcedTxPowerUsage(
        RealAntennaDigital tx) {
      return NoPowerUsage;
     }

    public virtual SpectrumBreakdown SourcedSpectrumUsage(
        RealAntennaDigital tx) {
      return NoSpectrumUsage;
    }
    public virtual IEnumerable<RealAntennaDigital> Transmitters() { yield break; }
    public virtual IEnumerable<RealAntennaDigital> Users() { yield break; }
    protected NetworkUsage() {}

    protected static PowerBreakdown NoPowerUsage = new PowerBreakdown();
    protected static SpectrumBreakdown NoSpectrumUsage = new SpectrumBreakdown();
  }

  public Routing() {
    heuristic = new RoutingPrecompute(this);
    current_network_usage_ = new RoutingNetworkUsage(this);
    Telecom.Instance?.RegisterRefreshMetric(reset_metric);
    Telecom.Instance?.RegisterRefreshMetric(one_hop_metric);
    Telecom.Instance?.RegisterRefreshMetric(a_star_metric);
    Telecom.Instance?.RegisterRefreshMetric(shortest_path_metric);
    Telecom.Instance?.RegisterRefreshMetric(dijkstras_metric);
    Telecom.Instance?.RegisterRefreshMetric(link_usage_metric);
    Telecom.Instance?.RegisterRefreshMetric(vgv_routing_metric);
    Telecom.Instance?.RegisterRefreshMetric(vgv_dijkstras_metric);
    Telecom.Instance?.RegisterRefreshMetric(find_channels_metric);
    Telecom.Instance?.RegisterRefreshMetric(find_channels_duplex_metric);
    Telecom.Instance?.RegisterRefreshMetric(find_channels_ptmp_metric);
  }

  public void Reset(IEnumerable<RACommNode> tx_only,
                    IEnumerable<RACommNode> rx_only,
                    IEnumerable<RACommNode> multiple_tracking_tx) {
    OrientedLink.ReturnLinks(this);
    links_.Clear();
    RoutingPrecompute.VGVLink.ReturnLinks(this);
    current_network_usage_.Clear();
    heuristic.InvalidateCache();

    tx_only_ = new HashSet<RACommNode>(tx_only);
    rx_only_ = new HashSet<RACommNode>(rx_only);
    multiple_tracking_ = new HashSet<RACommNode>(multiple_tracking_tx);
  }
  public NetworkUsage usage => current_network_usage_;

  public bool IsLimited(RACommNode node) {
    return !multiple_tracking_.Contains(node);
  }

  public Circuit FindCircuitInIsolation(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate) {
    return FindCircuit(source,
                       destination,
                       round_trip_latency_limit,
                       one_way_data_rate,
                       NetworkUsage.None);
  }

  public Circuit FindAndUseAvailableCircuit(
      RACommNode source,
      RACommNode destination,
      double round_trip_latency_limit,
      double one_way_data_rate,
      Connection connection) {
    Circuit circuit = FindCircuit(
        source,
        destination,
        round_trip_latency_limit,
        one_way_data_rate,
        current_network_usage_);
    if (circuit != null) {
      link_usage_metric.Start();
      foreach (OrientedLink link in circuit.forward.links) {
        current_network_usage_.UseLinkNoBroadcast(
            new SourcedLink(connection, circuit.forward, link),
            one_way_data_rate);
      }
      foreach (OrientedLink link in circuit.backward.links) {
        current_network_usage_.UseLinkNoBroadcast(
            new SourcedLink(connection, circuit.backward, link),
            one_way_data_rate);
      }
      link_usage_metric.StopSuccess();
    }
    return circuit;
  }

  public PointToMultipointAvailability FindChannelsInIsolation(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels) {
    return FindChannels(source,
                        destinations,
                        latency_limit,
                        data_rate,
                        NetworkUsage.None,
                        out channels);
  }

  public PointToMultipointAvailability FindAndUseAvailableChannels(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      out Channel[] channels,
      Connection connection) {
    PointToMultipointAvailability availability = FindChannels(
        source,
        destinations,
        latency_limit,
        data_rate,
        current_network_usage_,
        out channels);
    if (availability != Unavailable) {
      link_usage_metric.Start();
      var links_by_tx_antenna =
          from channel in channels where channel != null
          from link in channel.links
          group new SourcedLink(connection, channel, link) by link.tx_antenna;
      foreach (var links in links_by_tx_antenna) {
        current_network_usage_.UseLinks(links.ToList(), data_rate);
      }
      link_usage_metric.StopSuccess();
    }
    return availability;
  }

  private Circuit FindCircuit(RACommNode source,
                              RACommNode destination,
                              double round_trip_latency_limit,
                              double one_way_data_rate,
                              NetworkUsage usage) {
    if (FindChannel(source,
                     destination,
                     round_trip_latency_limit, 
                     one_way_data_rate,
                     usage,
                     out Channel forward) == Unavailable) {
      return null;
    }
    fake_usage_metric.Start();
    RoutingNetworkUsage current_usage = (usage != NetworkUsage.None) ? (RoutingNetworkUsage) usage : new RoutingNetworkUsage(this, usage);
    foreach (var link in forward.links) {
      current_usage.UseLinkNoBroadcast(link.Unsourced(), one_way_data_rate, fake: true);
    }
    fake_usage_metric.Pause();
    if (FindChannel(destination,
                     source,
                     round_trip_latency_limit - forward.latency,
                     one_way_data_rate,
                     current_usage,
                     out Channel backward) == Unavailable) {
        
      fake_usage_metric.Resume();
        foreach (var link in forward.links) {
          current_usage.RemoveFakeLink(link.Unsourced());
        }
        fake_usage_metric.StopSuccess();
      return null;
    }
    fake_usage_metric.Resume();
    foreach (var link in forward.links) {
      current_usage.RemoveFakeLink(link.Unsourced());
    }
    fake_usage_metric.StopSuccess();
    return new Circuit(forward, backward);
  }

  private PointToMultipointAvailability FindChannel(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    find_channels_metric.Start();
    PointToMultipointAvailability res = Unavailable;
    channel = null;
    switch (method) {
      case RoutingMethod.VGV:
        res = FindChannelVGV(source, destination, latency_limit, data_rate, usage, out channel);
        break;
      case RoutingMethod.ASTAR:
        res = FindChannelsAPSP(source, destination, latency_limit, data_rate, usage, out channel);
        break;
      case RoutingMethod.ONEHOP:
        if (FindChannelsOneHop(source, destination, latency_limit, data_rate, usage, out channel) == PointToMultipointAvailability.Available) {
          res = PointToMultipointAvailability.Available;
          break;
        }
        goto case RoutingMethod.DIJKSTRAS; // Fall back to Dijkstra's if this fails.
      case RoutingMethod.DIJKSTRAS:
        Channel[] channels;
        res = FindChannelsDijkstras(source, new[] {destination}, latency_limit, data_rate, usage, out channels);
        channel = channels[0];
        break;
      default:
        Telecom.Log($"Routing method {method} not any legal method!!");
        break;
    }
    find_channels_metric.StopSuccess();
    return res;
  }

  private PointToMultipointAvailability FindChannels(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel[] channels) {
    if (destinations.Count == 1) {
      channels = new Channel[1];
      var res2 = FindChannel(source, destinations[0], latency_limit, data_rate, usage, out channels[0]);
      return res2;
    }
    find_channels_metric.Start();
    if (method == RoutingMethod.VGV) {
      var res3 = FindChannelsVGV(source, destinations, latency_limit, data_rate, usage, out channels);
      find_channels_metric.StopSuccess();
      return res3;
    }
    var res = FindChannelsDijkstras(source, destinations, latency_limit, data_rate, usage, out channels);
    find_channels_metric.StopSuccess();
    return res;
  }

  private PointToMultipointAvailability FindChannelsDijkstras(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel[] channels) {
    dijkstras_metric.Start();
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new PriorityQueue<RACommNode, double>();
    var interior = new HashSet<RACommNode>();
    
    // Dijkstra’s algorithm without DecreaseKey.
    distances[source] = 0;
    boundary.Enqueue(source, 0);
    previous[source] = null;
    int rx_found = 0;
    channels = new Channel[destinations.Count];
    bool is_point_to_multipoint = destinations.Count > 1;

    double[] destination_distances = new double[destinations.Count];

    for (int i = destinations.Count - 1; i >= 0; --i) {
      destination_distances[i] = double.PositiveInfinity;
    }

    while (boundary.TryDequeue(out RACommNode tx, out double tx_distance)) {
      if (tx_distance != distances[tx]) {
        // We have already considered `tx` through a shorter path.
        continue;
      }
      if (destinations.Contains(tx)) {
        int i = destinations.IndexOf(tx);
        channels[i] = new Channel();
        for (OrientedLink link = previous[tx];
             link != null;
             link = previous[link.tx]) {
           channels[i].links.Add(link);
        }
        channels[i].links.Reverse();
        channels[i].latency = tx_distance / c;
        ++rx_found;
        if (rx_found == channels.Length) {
          dijkstras_metric.StopSuccess();
          return PointToMultipointAvailability.Available;
        }
      } else if (tx_distance > latency_distance) {
        // We have run out of latency, no need to keep searching.
        dijkstras_metric.StopFailure();
        return rx_found == 0 ? Unavailable : Partial;
      } 

      interior.Add(tx);

      if (rx_only_.Contains(tx)) {
        continue;
      }

      foreach (var stock_rx in tx.Keys) {
        var rx = (RACommNode)stock_rx;

        if (tx_only_.Contains(rx) || interior.Contains(rx)) {
          continue;
        }

        double tentative_distance = distances[tx] + (tx.precisePosition - rx.precisePosition).magnitude; 
        if (tentative_distance > latency_distance || 
            (distances.TryGetValue(rx, out double d) &&
            tentative_distance > d)) { // Latency optimality check
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);
        if (link.max_data_rate < data_rate) { // Best-case data rate check.
          continue;
        }
        
        if (!link.CheckCapacityWithUsage(usage, data_rate)) {
          continue;
        }
        distances[rx] = tentative_distance;
        previous[rx] = link;
        boundary.Enqueue(rx, tentative_distance);
        int ind = destinations.IndexOf(rx);
        if (ind != -1) {
          destination_distances[ind] = tentative_distance;
          double worst_dist = destination_distances.Max();
          if (latency_distance > worst_dist) latency_distance = worst_dist;
        }
      }
    }
    dijkstras_metric.StopFailure();
    return rx_found == 0 ? Unavailable : Partial;
  }

  private PointToMultipointAvailability FindChannelsAPSP(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    shortest_path_metric.Start();
    if (TryShortestPath(source, destination, latency_limit, data_rate, usage, out Channel ans) == PointToMultipointAvailability.Available) {
      channel = ans;
      shortest_path_metric.StopSuccess();
      return PointToMultipointAvailability.Available;
    }
    shortest_path_metric.StopFailure();
    a_star_metric.Start();
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    // TODO(egg): consider using the stock intrusive data structure.
    var distances = new Dictionary<RACommNode, double>();
    var previous = new Dictionary<RACommNode, OrientedLink>();
    var boundary = new PriorityQueue<RACommNode, double>();
    var interior = new HashSet<RACommNode>();
    a_star_metric.Pause();
    heuristic.GenerateShortestPaths();
    a_star_metric.Resume();
    //var metrics = Telecom.Instance.runtimeMetrics_;
    //metrics.apsp_routes++;
    // Dijkstra’s algorithm without DecreaseKey.
    distances[source] = heuristic.GetHeuristicDistance(source, destination);
    //Telecom.Log($"{source.displayName} -> {destination.displayName} heuristic distance: {distances[source]}");
    boundary.Enqueue(source, distances[source]);
    previous[source] = null;
    channel = new Channel();
    while (boundary.TryDequeue(out RACommNode tx, out double tx_distance)) {
      if (tx_distance != distances[tx]) {
        // We have already considered `tx` through a shorter path.
        continue;
      }
      if (destination == tx) {
        for (OrientedLink link = previous[tx];
             link != null;
             link = previous[link.tx]) {
           channel.links.Add(link);
        }
        channel.links.Reverse();
        channel.latency = tx_distance / c;
        a_star_metric.StopSuccess();
        return PointToMultipointAvailability.Available;
      } else if (tx_distance > latency_distance) {
        // We have run out of latency, no need to keep searching.
        channel = null;
        a_star_metric.StopFailure();
        return PointToMultipointAvailability.Unavailable;
      } 

      interior.Add(tx);

      if (rx_only_.Contains(tx)) {
        continue;
      }

      double tx_node_penalty = heuristic.GetHeuristicDistance(tx, destination);

      foreach (var stock_rx in tx.Keys) {
        var rx = (RACommNode)stock_rx;

        if (tx_only_.Contains(rx) || interior.Contains(rx) || !heuristic.IsGoodNode(rx)) {
          continue;
        }

        double tentative_distance = distances[tx] + (tx.precisePosition - rx.precisePosition).magnitude + heuristic.GetHeuristicDistance(rx, destination) - tx_node_penalty; 
        if (tentative_distance > latency_distance || 
            (distances.TryGetValue(rx, out double d) &&
            tentative_distance > d)) { // Latency optimality check
          continue;
        }

        var link = OrientedLink.Get(this, from: tx, to: rx);
        if (link.max_data_rate < data_rate) { // Best-case data rate check.
          continue;
        }

        if (!link.CheckCapacityWithUsage(usage, data_rate)) {
          continue;
        }
        distances[rx] = tentative_distance;
        previous[rx] = link;
        boundary.Enqueue(rx, tentative_distance);
        if (rx == destination) latency_distance = tentative_distance; // Don't consider any links with no chance of improving our current solution.
        //metrics.apsp_links_considered++;
      }
    }
    channel = null;
    a_star_metric.StopFailure();
    return PointToMultipointAvailability.Unavailable;
  }

  private PointToMultipointAvailability TryShortestPath(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    shortest_path_metric.Pause();
    heuristic.GenerateShortestPaths();
    shortest_path_metric.Resume();
    channel = new Channel();
    double tx_distance = heuristic.GetHeuristicDistance(source, destination);
    if (tx_distance > latency_distance) {
      return PointToMultipointAvailability.Unavailable;
    }
    RACommNode prev = source;
    var path = heuristic.BestRoute(source, destination);
    
    foreach (RACommNode node in path) {
      var link = OrientedLink.Get(this, from: prev, to: node);
      if (link.max_data_rate < data_rate) {
        channel = null;
        return PointToMultipointAvailability.Unavailable;
      }
      if (!link.CheckCapacityWithUsage(usage, data_rate)) {
        channel = null;
        return PointToMultipointAvailability.Unavailable;
      }
      channel.links.Add(link);
      channel.latency = tx_distance / c;
      prev = node;
    }
    return PointToMultipointAvailability.Available;
  }

  private PointToMultipointAvailability FindChannelsOneHop(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    one_hop_metric.Start();
    const double c = 299792458;
    double latency_distance = c * latency_limit;
    channel = new Channel();
    double best_distance = latency_distance;
    foreach (RACommNode relay in source.Keys) {
      if (relay.TryGetValue(destination, out var outbound)) {
        OrientedLink outbound_link = OrientedLink.Get(this, from: relay, to: destination);
        if (outbound_link.max_data_rate < data_rate) {
          continue;
        }

        OrientedLink inbound_link = OrientedLink.Get(this, from: source, to: relay);
        if (inbound_link.max_data_rate < data_rate) {
          continue;
        }

        double distance = outbound_link.length + inbound_link.length;
        if (distance > best_distance) {
          continue;
        }

        if (!outbound_link.CheckCapacityWithUsage(usage, data_rate)) {
          continue;
        }

        if (!inbound_link.CheckCapacityWithUsage(usage, data_rate)) {
          continue;
        }

        best_distance = distance;
        channel.links.Clear();
        channel.links.Add(inbound_link);
        channel.links.Add(outbound_link);
      }
    }
    if (best_distance < latency_distance) {
      channel.latency = best_distance / c;
      one_hop_metric.StopSuccess();
      return PointToMultipointAvailability.Available;
    } else {
      channel = null;
      one_hop_metric.StopFailure();
      return PointToMultipointAvailability.Unavailable;
    }
  }

  private PointToMultipointAvailability FindChannelVGV(
      RACommNode source,
      RACommNode destination,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel channel) {
    const double c = 299792458;
    heuristic.PopulateVGVLinks();
    vgv_routing_metric.Start();
    double latency_distance = c * latency_limit;
    // TODO(egg): consider using the stock intrusive data structure.
    int vessel_count = heuristic.vessels.Count;
    var distances = new double[vessel_count];
    var ingress = new OrientedLink[vessel_count]; // For each vessel, what link do we take to enter the network?
    var previous = new RoutingPrecompute.VGVLink[vessel_count];
    var explored = new bool[vessel_count];
    OrientedLink egress = null; // The final link we take to the destination.
    int vessel_index_to_explore = -1;

    for (int vessel_index = vessel_count - 1; vessel_index >= 0; --vessel_index) {
      var rx = heuristic.vessels[vessel_index];
      distances[vessel_index] = latency_distance;
      ingress[vessel_index] = null;
      explored[vessel_index] = false;
      previous[vessel_index] = null;
      if (source.ContainsKey(rx)) {
        var link = OrientedLink.Get(this, from: source, to: rx);
        var tentative_distance = link.length + heuristic.GetVGHeuristicDistance(tx: rx, rx: destination);
        if (link.CheckCapacityWithUsage(usage, data_rate) && tentative_distance <= latency_distance) {
          distances[vessel_index] = tentative_distance;
          ingress[vessel_index] = link;
          previous[vessel_index] = null;
          if (vessel_index_to_explore == -1 || distances[vessel_index_to_explore] > distances[vessel_index]) {
            vessel_index_to_explore = vessel_index;
          }
        }
      }
    }

    while (vessel_index_to_explore != -1) {
      var tx = heuristic.vessels[vessel_index_to_explore];
      var tx_distance = distances[vessel_index_to_explore];
      if (tx_distance > latency_distance) {
        // We have run out of latency, no need to keep searching.
        break;
      } 
      double tx_node_penalty = heuristic.GetVGHeuristicDistance(tx, destination);

      if (tx.ContainsKey(destination)) {
        var egress_link = OrientedLink.Get(this, from: tx, to: destination);
        if (egress_link.CheckCapacityWithUsage(usage, data_rate)) {
          var final_distance = tx_distance - tx_node_penalty + egress_link.length;
          if (final_distance <= latency_distance) {
            egress = egress_link;
            latency_distance = final_distance;
          }
        }
      }
      explored[vessel_index_to_explore] = true;

      foreach (int rx_index in heuristic.GetLinkedIndices(vessel_index_to_explore)) {
        if (explored[rx_index]) {
          continue;
        }
        var rx = heuristic.vessels[rx_index];
        double distance_limit = distances[rx_index] - distances[vessel_index_to_explore] + tx_node_penalty - heuristic.GetVGHeuristicDistance(rx, destination);
        var links = heuristic.vgv_links[vessel_index_to_explore, rx_index];
        var best_distance = links[0].distance;
        if (best_distance >= distance_limit) continue;
        var best_link = links.TakeWhile(link => link.distance <= distance_limit).Where(link => link.CheckCapacityWithUsage(usage, data_rate)).FirstOrDefault();
        if (best_link is null) continue;
        double tentative_distance = distances[vessel_index_to_explore] + best_link.distance - tx_node_penalty + heuristic.GetVGHeuristicDistance(rx, destination);
        distances[rx_index] = tentative_distance;
        previous[rx_index] = best_link;
      }
      vessel_index_to_explore = -1;
        
      for (int i = vessel_count - 1; i >= 0; --i) {
        if (!explored[i] && (vessel_index_to_explore == -1 || distances[vessel_index_to_explore] > distances[i])) {
          vessel_index_to_explore = i;
        }
      }
    }
    if (!(egress is null)) {
      channel = new Channel();
      channel.links.Add(egress);
      RACommNode prev = egress.tx;
      for (RoutingPrecompute.VGVLink vgv_link = previous[heuristic.vessel_ordering[egress.tx]];
            vgv_link != null;
            vgv_link = previous[heuristic.vessel_ordering[vgv_link.source]]) {
        if (!(vgv_link.link2_ is null)) { 
          channel.links.Add(vgv_link.link2_);
        }
        channel.links.Add(vgv_link.link1_);
        prev = vgv_link.source;
      }
      channel.links.Add(ingress[heuristic.vessel_ordering[prev]]);
      channel.links.Reverse();
      channel.latency = latency_distance / c;
      vgv_routing_metric.StopSuccess();
      return Available;
    }
    channel = null;
    vgv_routing_metric.StopFailure();
    return PointToMultipointAvailability.Unavailable;
  }

  private PointToMultipointAvailability FindChannelsVGV(
      RACommNode source,
      IList<RACommNode> destinations,
      double latency_limit,
      double data_rate,
      NetworkUsage usage,
      out Channel[] channels) {
    heuristic.PopulateVGVLinks();
    vgv_dijkstras_metric.Start();
    const double c = 299792458;
    double latency_distance = c * latency_limit;

    // VGV explores a dense graph with a small number of nodes. Therefore it actually makes more sense to ditch the priority queue entirely.
    int vessel_count = heuristic.vessels.Count;
    var distances = new double[vessel_count];
    var ingress = new OrientedLink[vessel_count];
    var previous = new RoutingPrecompute.VGVLink[vessel_count];
    var explored = new bool[vessel_count];
    var egress = new OrientedLink[destinations.Count];
    int vessel_index_to_explore = -1;

    for (int vessel_index = vessel_count - 1; vessel_index >= 0; --vessel_index) {
      var rx = heuristic.vessels[vessel_index];
      distances[vessel_index] = double.PositiveInfinity;
      ingress[vessel_index] = null;
      explored[vessel_index] = false;
      previous[vessel_index] = null;
      if (!source.ContainsKey(rx)) {
        continue; 
      }
      var link = OrientedLink.Get(this, from: source, to: rx);
      if (!link.CheckCapacityWithUsage(usage, data_rate) || link.length > latency_distance) {
        continue;
      }
      distances[vessel_index] = link.length;
      ingress[vessel_index] = link;
      previous[vessel_index] = null;
      if (vessel_index_to_explore == -1 || distances[vessel_index_to_explore] > distances[vessel_index]) {
        vessel_index_to_explore = vessel_index;
      }
    }

    double[] destination_distances = new double[destinations.Count];

    for (int i = destinations.Count - 1; i >= 0; --i) {
      destination_distances[i] = latency_distance;
    }

    int rx_found = 0;
    channels = new Channel[destinations.Count];
    while (vessel_index_to_explore != -1) {
      var tx = heuristic.vessels[vessel_index_to_explore];
      var tx_distance = distances[vessel_index_to_explore];
      if (tx_distance > latency_distance) {
        // We have run out of latency, no need to keep searching.
        break;
      } 
      
      for (int destination_index = destinations.Count - 1; destination_index >= 0; --destination_index) {
        RACommNode destination = destinations[destination_index];
        if (tx.ContainsKey(destination)) {
          var egress_link = OrientedLink.Get(this, from: tx, to: destination);
          if (egress_link.CheckCapacityWithUsage(usage, data_rate)) {
            var final_distance = tx_distance + egress_link.length;
            if (final_distance <= destination_distances[destination_index]) {
              if (egress[destination_index] is null) rx_found++;
              egress[destination_index] = egress_link;
              destination_distances[destination_index] = final_distance;
              latency_distance = destination_distances.Max();
            }
          }
        }
      }

      explored[vessel_index_to_explore] = true;

      foreach (int rx_index in heuristic.GetLinkedIndices(vessel_index_to_explore)) {
        if (explored[rx_index]) {
          continue;
        }
        double distance_limit = latency_distance;
        if (distances[rx_index] != double.PositiveInfinity) {
          distance_limit = (latency_distance > distances[rx_index]) ? distances[rx_index] : latency_distance;
        }
        distance_limit -= distances[vessel_index_to_explore];
        if (!heuristic.TryGetShortestLinkWithUsage(tx: vessel_index_to_explore, rx: rx_index, max_distance: distance_limit, usage, min_data_rate: data_rate, out RoutingPrecompute.VGVLink best_link)) {
          continue;
        }
        double tentative_distance = distances[vessel_index_to_explore] + best_link.distance;
        distances[rx_index] = tentative_distance;
        previous[rx_index] = best_link; 
      }
      vessel_index_to_explore = -1;
        
      for (int i = vessel_count - 1; i >= 0; --i) {
        if (!explored[i] && (vessel_index_to_explore == -1 || distances[vessel_index_to_explore] > distances[i])) {
          vessel_index_to_explore = i;
        }
      }
    }
    
    channels = new Channel[destinations.Count];
    for (int destination_index = destinations.Count - 1; destination_index >= 0; --destination_index) {
      if (egress[destination_index] is null) continue;
      channels[destination_index] = new Channel();
      channels[destination_index].links.Add(egress[destination_index]);
      RACommNode prev = egress[destination_index].tx;
      for (RoutingPrecompute.VGVLink vgv_link = previous[heuristic.vessel_ordering[prev]];
            vgv_link != null;
            vgv_link = previous[heuristic.vessel_ordering[vgv_link.source]]) {
        if (!(vgv_link.link2_ is null)) { 
          channels[destination_index].links.Add(vgv_link.link2_);
        }
        channels[destination_index].links.Add(vgv_link.link1_);
        prev = vgv_link.source;
      }
      channels[destination_index].links.Add(ingress[heuristic.vessel_ordering[prev]]);
      channels[destination_index].links.Reverse();
      channels[destination_index].latency = destination_distances[destination_index] / c;
    }
    if (rx_found == destinations.Count) {
      vgv_dijkstras_metric.StopSuccess();
      return Available;
    }
    vgv_dijkstras_metric.StopFailure();
    return rx_found == 0 ? Unavailable : Partial;
  }  

  public class RoutingPrecompute {
    // All-pairs shortest paths
    //private ProfilerMarker profiler = new ProfilerMarker("Floyd-Warshall");

    public RoutingPrecompute(Routing routing) {
      Telecom.Instance?.RegisterRefreshMetric(apsp_metric);
      Telecom.Instance?.RegisterRefreshMetric(vgv_precompute_metric);
      routing_ = routing;
    }

    public void FindNodes(double bandwidth_filter = 1e6) {
      var home_body = FlightGlobals.GetHomeBody();
      nodes.Clear();
      ordering.Clear();

      foreach (RACommNode node in (CommNet.CommNetNetwork.Instance?.CommNet as RACommNetwork).Nodes) {
        if ((node.ParentBody == home_body || node.ParentVessel?.mainBody == home_body) && 
            node.RAAntennaList.Any(ra => ra.RFBand.ChannelWidth >= bandwidth_filter)) {
          // Only consider ground stations and vessels with a wideband antenna on them.
          ordering[node] = nodes.Count;
          nodes.Add(node);
        }
      }
    }

    public void OverrideNodes(List<RACommNode> nodes) {
      this.nodes.Clear();
      for (int i = 0; i < nodes.Count; ++i) {
        this.nodes.Add(nodes[i]);
        ordering[nodes[i]] = i;
      }
    }

    public void GenerateShortestPaths(double minimum_link_data_rate = 1e2) {
      if (cached) return;
      apsp_metric.Start();
      if (nodes.Count == 0) FindNodes();
      //profiler.Begin();
      
      //Telecom.Log($"Found {nodes.Count} relevant stations and vessels.");

      int N = nodes.Count;
      shortest_path = new double[N, N];
      path_forwardtrace = new int[N, N];
      for (int i = N - 1; i >= 0; --i) {
        for (int j = N - 1; j >= 0; --j) {
          shortest_path[i, j] = double.PositiveInfinity;
          path_forwardtrace[i, j] = -1;
        }
        shortest_path[i, i] = 0;
        path_forwardtrace[i, i] = i;
        var tx = nodes[i];
        foreach (RACommNode rx in tx.Keys) {
          if (ordering.TryGetValue(rx, out int j)) {
            RACommLink ra_link = (RACommLink) tx[rx];
            if (ra_link.a == tx && ra_link.FwdDataRate < minimum_link_data_rate) continue;
            else if (ra_link.b == tx && ra_link.RevDataRate < minimum_link_data_rate) continue;
            shortest_path[i, j] = (tx.precisePosition - rx.precisePosition).magnitude; 
            path_forwardtrace[i, j] = j;
          }
        }
      }
      for (int k = N - 1; k >= 0; --k) {
        for (int i = N - 1; i >= 0; --i) {
          if (shortest_path[i, k] != double.PositiveInfinity) {
            for (int j = N - 1; j >= 0; --j) {
              if (shortest_path[k, j] != double.PositiveInfinity && shortest_path[i, j] > shortest_path[i, k] + shortest_path[k, j]) {
                shortest_path[i, j] = shortest_path[i, k] + shortest_path[k, j];
                path_forwardtrace[i, j] = path_forwardtrace[i, k];
              }
            }
          }
        }
      }
      cached = true;
      //profiler.End();
      apsp_metric.StopSuccess();
    }
    public double GetHeuristicDistance(RACommNode tx, RACommNode rx) {
      if (ordering.TryGetValue(tx, out int i) && ordering.TryGetValue(rx, out int j)) {
        return shortest_path[i, j];   
      }
      //Telecom.Log($"{tx.displayName} -> {rx.displayName} not in network!!");
      return double.PositiveInfinity; // This is not in our "good" node network!
    }

    public bool IsGoodNode(RACommNode node) {
      return ordering.ContainsKey(node);
    }

    public IEnumerable<RACommNode> BestRoute(RACommNode tx, RACommNode rx) {
      // Skips yielding the first node (tx) for ease of implementation elsewhere.
      if (ordering.TryGetValue(tx, out int i) && ordering.TryGetValue(rx, out int j) && path_forwardtrace[i, j] != -1) {
        while (i != j) {
          i = path_forwardtrace[i, j];
          yield return nodes[i];
        }
      } 
      yield break;
    }

    public void InvalidateCache() {
      nodes.Clear();
      ordering.Clear();
      vessels.Clear();
      vessel_ordering.Clear();
      vg_fresh.Clear(); // So we don't have to reallocate double[]s.
      shortest_path = null;
      path_forwardtrace = null;
      cached = false;
      cached_vgv = false;
    }

    

    private readonly Dictionary<RACommNode, int> ordering = new Dictionary<RACommNode, int>(256);
    private readonly List<RACommNode> nodes = new List<RACommNode>();
    private double[,] shortest_path;
    private int[,] path_forwardtrace;
    private bool cached = false;
    internal PerRefreshMetric apsp_metric = new PerRefreshMetric("Floyd-Warshall");

    public class VGVLink {
      // Short for Vessel-Ground-Vessel.
      // Represents an abstract connection between two vessels, possibly linked by a relaying ground station in between.
      private static readonly Queue<VGVLink> pool = new Queue<VGVLink>(); // Borrowing the link pooling from OrientedLink.
      private static VGVLink GetFromPool() => pool.Count > 0 ? pool.Dequeue() : new VGVLink();
      internal static void ReturnLinks(Routing r) {
        while (r.vgv_links.TryDequeue(out VGVLink link)) {
          link.Clear();
          pool.Enqueue(link);
        }
      }
      public static VGVLink Get(Routing routing, RACommNode source, RACommNode intermediary, RACommNode destination) {
        VGVLink link = GetFromPool();
        if (intermediary is null) {
          link.link1_ = OrientedLink.Get(routing, from: source, to: destination);
          link.distance = link.link1_.length;
          link.max_data_rate = link.link1_.max_data_rate;
        } else {
          link.link1_ = OrientedLink.Get(routing, from: source, to: intermediary);
          link.link2_ = OrientedLink.Get(routing, from: intermediary, to: destination);
          link.distance = link.link1_.length + link.link2_.length;
          link.max_data_rate = (link.link1_.max_data_rate > link.link2_.max_data_rate) ? link.link2_.max_data_rate : link.link1_.max_data_rate;
        }
        return link;
      }
      
      public static VGVLink Get(Routing routing, OrientedLink link1, OrientedLink link2) {
        VGVLink link = GetFromPool();
        link.link1_ = link1;
        link.link2_ = link2;
        link.distance = link.link1_.length + link.link2_.length;
        link.max_data_rate = (link.link1_.max_data_rate > link.link2_.max_data_rate) ? link.link2_.max_data_rate : link.link1_.max_data_rate;
        return link;
      }

      public void Clear() {
        link1_ = null;
        link2_ = null;
      }

      public bool CheckCapacityWithUsage(NetworkUsage usage, double data_rate) {
        if (link2_ is null) {
          return link1_.CheckCapacityWithUsage(usage, data_rate);
        } else {
          return data_rate < max_data_rate && link1_.CheckTxCapacityWithUsage(usage, data_rate) && link2_.CheckRxCapacityWithUsage(usage, data_rate);
          // We can omit the intermediary check since groundstations are all multi-tracking, so we just check against our precomputed max_data-rate.
        }
      }

      public IEnumerable<OrientedLink> LinksReverse() {
        if (!(link2_ is null)) yield return link2_;
        yield return link1_;
      }
      
      public RACommNode source => link1_.tx;
      public RACommNode intermediary => (link2_ is null) ? null : link2_.tx;
      public RACommNode destination => (link2_ is null) ? link1_.rx : link2_.rx;
      public OrientedLink link1_, link2_;
      public RealAntennaDigital outbound_antenna => link1_.tx_antenna;
      public RealAntennaDigital inbound_antenna => (link2_ is null) ? link1_.rx_antenna : link2_.rx_antenna;
      public double distance {get; private set; } = 0;
      public double max_data_rate {get; private set; } = 0;
    }

    public void FindVessels(double bandwidth_filter = 1e6) {
      var home_body = FlightGlobals.GetHomeBody();
      vessels.Clear();

      vessel_ordering.Clear();

      foreach (RACommNode node in (CommNet.CommNetNetwork.Instance?.CommNet as RACommNetwork).Nodes) {
        if ((node.ParentVessel?.mainBody == home_body) && 
            node.RAAntennaList.Any(ra => ra.RFBand.ChannelWidth >= bandwidth_filter)) {
          vessel_ordering[node] = vessels.Count;
          vessels.Add(node);
        }
      }
    }

    public void PopulateVGVLinks(double minimum_link_data_rate = 1e2) {
      if (cached_vgv) return;
      vgv_precompute_metric.Start();
      if (vessels.Count == 0) FindVessels();
      int N = vessels.Count;
      if (vgv_links?.LongLength != N * N) { // We only need to resize and allocate if the number of vessels actually changed.
        vgv_links = new List<VGVLink>[N, N];
        for (int i = N - 1; i >= 0; --i) {
          for (int j = N - 1; j >= 0; --j) {
            vgv_links[i, j] = new List<VGVLink>();
          }
        }
      }
      if (adjacency_list?.Length != N) {
        adjacency_list = new List<int>[N];
        for (int i = N - 1; i >= 0; --i) {
          adjacency_list[i] = new List<int>();
        }
      }
      for (int i = N - 1; i >= 0; --i) {
        adjacency_list[i].Clear();
        for (int j = N - 1; j >= 0; --j) {
          if (i == j) {
            continue;
          }
          vgv_links[i, j].Clear();
          if (vessels[i].ContainsKey(vessels[j])) {
            VGVLink link = VGVLink.Get(routing_, vessels[i], null, vessels[j]);
            if (link.CheckCapacityWithUsage(NetworkUsage.None, minimum_link_data_rate)) {
              vgv_links[i, j].Add(link);  
            }
          }
        }
      }
      for (int i = N - 1; i >= 0; --i) {
        RACommNode source = vessels[i];
        foreach (RACommNode intermediary in source.Keys) { 
          if (vessel_ordering.ContainsKey(intermediary)) continue;
          OrientedLink outbound = OrientedLink.Get(routing_, from: source, to: intermediary);
          if (outbound.max_data_rate < minimum_link_data_rate) continue;
          for (int j = N - 1; j >= 0; --j) { // Yes, looping over vessels ends up faster than looping over Dictionary keys.
            if (i == j) continue;
            RACommNode destination = vessels[j];
            if (intermediary.ContainsKey(destination)) {
              OrientedLink inbound = OrientedLink.Get(routing_, from: intermediary, to: destination);
              if (inbound.max_data_rate < minimum_link_data_rate) continue;
              VGVLink link = VGVLink.Get(routing_, outbound, inbound);
              vgv_links[i, j].Add(link);
            }
          }
        }
      }
      for (int i = N - 1; i >= 0; --i) {
        for (int j = N - 1; j >= 0; --j) {
          if (i == j) {
            continue;
          }
          vgv_links[i, j] = vgv_links[i, j].OrderByDescending(link => link.distance).ThenBy(link => link.max_data_rate).ToList();
          // Reverse order, because I like reverse iteration.
          if (vgv_links[i, j].Count > 0) {
            adjacency_list[i].Add(j);
          }
        }
      }
      CalculateShortestVVPaths();
      vgv_precompute_metric.StopSuccess();
      cached_vgv = true;
    }

    public void CalculateShortestVVPaths() {
      int N = vessels.Count;
      if (shortest_vv_paths?.LongLength != N * N) {
        shortest_vv_paths = new double[N, N];
      }
      for (int i = N - 1; i >= 0; --i) {
        for (int j = N - 1; j >= 0; --j) {
          if (i == j) {
            continue;
          }
          shortest_vv_paths[i, j] = (vgv_links[i, j].Count > 0) ? vgv_links[i, j].First().distance : double.PositiveInfinity;
        }
        shortest_vv_paths[i, i] = 0;
      }
      for (int k = N - 1; k >= 0; --k) {
        for (int i = N - 1; i >= 0; --i) {
          if (shortest_vv_paths[i, k] != double.PositiveInfinity) {
            for (int j = N - 1; j >= 0; --j) {
              if (shortest_vv_paths[k, j] != double.PositiveInfinity && shortest_vv_paths[i, j] > shortest_vv_paths[i, k] + shortest_vv_paths[k, j]) {
                shortest_vv_paths[i, j] = shortest_vv_paths[i, k] + shortest_vv_paths[k, j];
              }
            }
          }
        }
      }
    }

    public bool TryGetShortestLinkWithUsage(int tx, int rx, double max_distance, NetworkUsage usage, double min_data_rate, out VGVLink link) {
      link = null;
      var links = vgv_links[tx, rx];
      for (int i = links.Count - 1; i >= 0; --i) {
        if (links[i].distance > max_distance) return false; // The links are sorted so we can shortcut here.
        if (links[i].CheckCapacityWithUsage(usage, min_data_rate)) {
          link = links[i];
          return true;
        }
      }
      return false;
    }

    public IList<int> GetLinkedIndices(int i) {
      return adjacency_list[i];
    }

    public double GetVGHeuristicDistance(RACommNode tx, RACommNode rx) {
      if (!vessel_ordering.TryGetValue(tx, out int i)) {
        return double.PositiveInfinity; // We do not intend to handle this case.
      }
      if (!(vg_fresh.TryGetValue(rx, out bool fresh)) || !fresh) {
        if (!vg_heuristic.TryGetValue(rx, out double[] distance) || distance.Length != vessels.Count) {
          distance = new double[vessels.Count];
        }
        for (int j = vessels.Count - 1; j >= 0; --j) {
          if (vessels[j].ContainsKey(rx)) {
            OrientedLink link = OrientedLink.Get(routing_, from: vessels[j], to: rx);
            if (link.max_data_rate > 1e2) {
              distance[j] = link.length;
            } else {
              distance[j] = double.PositiveInfinity;
            }
          } else {
            distance[j] = double.PositiveInfinity;
          }
        }
        for (int ind = vessels.Count - 1; ind >= 0; --ind) {
          if (distance[ind] == double.PositiveInfinity) {
            for (int j = vessels.Count - 1; j >= 0; --j) {
              double update = distance[j] + shortest_vv_paths[ind, j];
              if (update < distance[ind]) distance[ind] = update;
            }
          }
        }
        vg_heuristic[rx] = distance;
        vg_fresh[rx] = true;
      }
      return vg_heuristic[rx][i];
    }

    public readonly Dictionary<RACommNode, int> vessel_ordering = new Dictionary<RACommNode, int>(32);
    public readonly List<RACommNode> vessels = new List<RACommNode>();
    public List<VGVLink>[,] vgv_links;
    private List<int>[] adjacency_list;
    private double[,] shortest_vv_paths;
    private readonly Dictionary<RACommNode, double[]> vg_heuristic = new Dictionary<RACommNode, double[]>();
    private readonly Dictionary<RACommNode, bool> vg_fresh = new Dictionary<RACommNode, bool>();
    private bool cached_vgv = false;
    private Routing routing_;
    internal PerRefreshMetric vgv_precompute_metric = new PerRefreshMetric("VGV Link Precompute");
  }

  private class LinkUsage {
    public readonly DirectedLinkUsage forward = new DirectedLinkUsage();
    public readonly DirectedLinkUsage backward = new DirectedLinkUsage();
  }

  private class DirectedLinkUsage {
    public double data_rate = 0;
    public readonly List<PointToMultipointConnection> connections = new List<PointToMultipointConnection>();
  }

  private class RoutingNetworkUsage : NetworkUsage {
    public RoutingNetworkUsage(Routing routing) {
      routing_ = routing;
    }

    public RoutingNetworkUsage(Routing routing, NetworkUsage other)
        : this(routing) {
      if (other is RoutingNetworkUsage nontrival) {
        tx_power_usage_ = nontrival.tx_power_usage_.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Clone());
        spectrum_usage_ = nontrival.spectrum_usage_.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Clone());
      }
    } // Weakly clone (without usage details)

    public void Clear() {
      tx_power_usage_.Clear();
      spectrum_usage_.Clear();
    }

    public override PowerBreakdown SourcedTxPowerUsage(RealAntennaDigital tx) {
      if (tx_power_usage_.TryGetValue(tx, out PowerBreakdown usage)) {
        return usage;
      }
      return NoPowerUsage;
    }

    public override SpectrumBreakdown SourcedSpectrumUsage(
        RealAntennaDigital rx) {
      if (spectrum_usage_.TryGetValue(rx, out SpectrumBreakdown usage)) {
        return usage;
      }
      return NoSpectrumUsage;
    }

    public override IEnumerable<RealAntennaDigital> Transmitters() {
      foreach (var antenna in tx_power_usage_.Keys) {
        if (antenna is RealAntennaDigital digital) {
          yield return digital;
        }
      }
    }

    public override IEnumerable<RealAntennaDigital> Users() {
      foreach (var antenna in spectrum_usage_.Keys) {
        if (antenna is RealAntennaDigital digital) {
          yield return digital;
        }
      }
    }

    // The links must all share the same tx antenna and tech level.
    // Uses tx power corresponding to broadcast at the given data rate along
    // all of these links (thus at the power needed for the weakest link).
    // Also uses the necessary spectrum on all antennas involved.
    
    // The multiplier and fake attributes exist for simulation purposes
    // FindCircuit needs to act as if the forward links are already being used
    // while routing the backward links.
    // Therefore multiplier modifies the power/spectrum usage, and the fake flag indicates to not save the new usages
    public void UseLinks(IEnumerable<SourcedLink> links,
                         double data_rate,
                         bool fake = false) {
      EnsureSameTxAntennaAndTL(from sourced in links select sourced.link);
      UseTxPower(links, data_rate, fake);
      UseSpectrum(links, data_rate, fake);
    }

    private void UseTxPower(IEnumerable<SourcedLink> links,
                            double data_rate,
                            bool fake = false) {
      if (routing_.multiple_tracking_.Contains(links.First().link.tx)) {
        return;
      }
      RealAntennaDigital tx_antenna = links.First().link.tx_antenna;
      if (!tx_power_usage_.ContainsKey(tx_antenna)) {
        tx_power_usage_.Add(tx_antenna, new PowerBreakdown());
      }
      var usages = (from sourced in links
                    select new PowerBreakdown.SingleUsage{
                        link = sourced,
                        power = sourced.link.TxPowerUsageFromDataRate(data_rate),
                    }).ToArray();
      tx_power_usage_[tx_antenna].AddUsages(usages, fake);
    }

    private void UseSpectrum(IEnumerable<SourcedLink> links, double data_rate, bool fake = false) {
      double usage = links.First().link.SpectrumUsageFromDataRate(data_rate);
      foreach (var sourced in links.GroupBy(l => l.link.rx_antenna)) {
        RACommNode rx = sourced.First().link.rx;
        RealAntennaDigital rx_antenna = sourced.First().link.rx_antenna;
        if (routing_.multiple_tracking_.Contains(rx)) {
          continue;
        }
        if (!spectrum_usage_.ContainsKey(rx_antenna)) {
          spectrum_usage_.Add(rx_antenna, new SpectrumBreakdown());
        }
        spectrum_usage_[rx_antenna].AddUsages(
            (from link in sourced select
                new SpectrumBreakdown.SingleUsage{
                    link = link,
                    kind = SpectrumBreakdown.SingleUsage.Kind.Receive,
                    spectrum = usage,
            }).ToArray(), fake);
      }
      RealAntennaDigital tx_antenna = links.First().link.tx_antenna;
      if (routing_.multiple_tracking_.Contains(links.First().link.tx)) {
        return;
      }
      if (!spectrum_usage_.ContainsKey(tx_antenna)) {
        spectrum_usage_.Add(tx_antenna, new SpectrumBreakdown());
      }
      spectrum_usage_[tx_antenna].AddUsages(
          (from link in links select
            new SpectrumBreakdown.SingleUsage{
                link = link,
                kind = SpectrumBreakdown.SingleUsage.Kind.Transmit,
                spectrum = usage,
          }).ToArray(), fake);
    }

    // LINQ-free version of UseLinks optimized for single links.
    public void UseLinkNoBroadcast(SourcedLink link, double data_rate, bool fake = false) {
      double spectrum_usage = link.link.SpectrumUsageFromDataRate(data_rate);
      
      // Rx spectrum
      if (!routing_.multiple_tracking_.Contains(link.link.rx)) {
        RealAntennaDigital rx_antenna = link.link.rx_antenna;
        if (!spectrum_usage_.ContainsKey(rx_antenna)) {
          spectrum_usage_.Add(rx_antenna, new SpectrumBreakdown());
        }
        spectrum_usage_[rx_antenna].AddUsages(new[] {
          new SpectrumBreakdown.SingleUsage{
                  link = link,
                  kind = SpectrumBreakdown.SingleUsage.Kind.Receive,
                  spectrum = spectrum_usage
        } }, fake);
      }

      if (!routing_.multiple_tracking_.Contains(link.link.tx)) {// Tx power
        RealAntennaDigital tx_antenna = link.link.tx_antenna;
        if (!tx_power_usage_.ContainsKey(tx_antenna)) {
          tx_power_usage_.Add(tx_antenna, new PowerBreakdown());
        }
        tx_power_usage_[tx_antenna].AddUsages(new [] {
          new PowerBreakdown.SingleUsage{
                  link = link,
                  power = link.link.TxPowerUsageFromDataRate(data_rate),
        } }, fake);
        if (!spectrum_usage_.ContainsKey(link.link.tx_antenna)) {
          spectrum_usage_.Add(tx_antenna, new SpectrumBreakdown());
        }
        spectrum_usage_[tx_antenna].AddUsages(new[] {
          new SpectrumBreakdown.SingleUsage{
                  link = link,
                  kind = SpectrumBreakdown.SingleUsage.Kind.Transmit,
                  spectrum = spectrum_usage
        } }, fake);
      }
    }

    public void RemoveFakeLink(SourcedLink link) {
      RealAntennaDigital rx_antenna = link.link.rx_antenna;
      if (spectrum_usage_.ContainsKey(rx_antenna)) {
        spectrum_usage_[rx_antenna].ResetFakeChanges();
      }
      RealAntennaDigital tx_antenna = link.link.tx_antenna;
      if (spectrum_usage_.ContainsKey(tx_antenna)) {
        spectrum_usage_[tx_antenna].ResetFakeChanges();
      }
      if (tx_power_usage_.ContainsKey(tx_antenna)) {
        tx_power_usage_[tx_antenna].ResetFakeChanges();
      }
    }

    private void EnsureSameTxAntennaAndTL(IEnumerable<OrientedLink> links) {
#if NEVER
      RealAntennaDigital tx_antenna = links.First().tx_antenna;
      var antennas = from link in links select link.tx_antenna;
      if (antennas.Any(tx => tx != tx_antenna)) {
        throw new ArgumentException("Broadcast from multiple antennas");
      }
      int tech_level = links.First().tech_level;
      var tech_levels = from link in links select link.tech_level;
      if (tech_levels.Any(tl => tl != tech_level)) {
        throw new ArgumentException("Broadcast at multiple tech levels");
      }
#endif
    }

    private readonly Dictionary<RealAntenna, PowerBreakdown> tx_power_usage_ =
        new Dictionary<RealAntenna, PowerBreakdown>();
    private readonly Dictionary<RealAntenna, SpectrumBreakdown> spectrum_usage_ =
        new Dictionary<RealAntenna, SpectrumBreakdown>();
    private Routing routing_;
  }

  public class OrientedLink {
    private static readonly Queue<OrientedLink> pool = new Queue<OrientedLink>();
    private static OrientedLink GetFromPool() => pool.Count > 0 ? pool.Dequeue() : new OrientedLink();
    internal static void ReturnLinks(Routing r) {
      foreach (var link in r.links_.Values) {
        link.Clear();
        pool.Enqueue(link);
      }
    }
    public static OrientedLink Get(
        Routing routing,
        RACommNode from,
        RACommNode to) {
      if (!routing.links_.TryGetValue((from, to), out OrientedLink link)) {
        var ra_link = (RACommLink)from[to];
        bool forward = ra_link.a == from;
        link = GetFromPool();
        link.Set(from, to, ra_link, forward, routing);
        routing.links_.Add((from, to), link);
      }
      return link;
    }

    public SourcedLink Unsourced() {
      return new SourcedLink(null, null, this);
    }

    public RACommNode tx { get; private set; }
    public RACommNode rx { get; private set; }
    public RACommLink ra_link { get; private set; }
    public bool forward { get; private set; }

    public RealAntennaDigital tx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaTx
                                     : ra_link.RevAntennaTx);
    public RealAntennaDigital rx_antenna =>
        (RealAntennaDigital)(forward ? ra_link.FwdAntennaRx
                                     : ra_link.RevAntennaRx);
    public double max_data_rate => forward ? ra_link.FwdDataRate
                                           : ra_link.RevDataRate;
    public int tech_level => Math.Min(tx_antenna.TechLevelInfo.Level,
                                      rx_antenna.TechLevelInfo.Level);
    public RealAntennas.Antenna.BandInfo band => tx_antenna.RFBand;
    // TODO(egg): we only care about encoding and modulation; but while TL 3 and
    // 4 have the same encoder, they differ in modulation (QPSK vs. 8PSK), so it
    // doesn’t matter that much.
    public bool is_at_tx_tech_level =>
        tech_level == tx_antenna.TechLevelInfo.Level;
    public RealAntennaDigital lowest_tech_antenna =>
        is_at_tx_tech_level ? tx_antenna : rx_antenna;
    public RAModulator modulator => lowest_tech_antenna.modulator;
    public RealAntennas.Antenna.Encoder encoder => lowest_tech_antenna.Encoder;
    // TODO(egg): this needs to be adapted once we have support for landlines.
    public double length => (tx.precisePosition - rx.precisePosition).magnitude;

    public bool CheckCapacityWithUsage(NetworkUsage usage, double data_rate) {
      // Tx power check.
      if (max_data_rate * (1 - usage.TxPowerUsage(tx_antenna)) < data_rate) {
        return false;
      }

      double max_used_data_rate = band.ChannelWidth * bits_per_symbol - data_rate;
      // Rx bandwidth check.
      if (usage.SpectrumUsage(rx_antenna) * bits_per_symbol > max_used_data_rate) {
        return false;
      }

      // Tx bandwidth check.
      if (usage.SpectrumUsage(tx_antenna) * bits_per_symbol > max_used_data_rate) {
        return false;
      }
      return true;
    }

    public bool CheckTxCapacityWithUsage(NetworkUsage usage, double data_rate) {
      // Tx power check.
      if (max_data_rate * (1 - usage.TxPowerUsage(tx_antenna)) < data_rate) {
        return false;
      }

      double max_used_data_rate = band.ChannelWidth * bits_per_symbol - data_rate;
      // Tx bandwidth check.
      if (usage.SpectrumUsage(tx_antenna) * bits_per_symbol > max_used_data_rate) {
        return false;
      }
      return true;
    }

    public bool CheckRxCapacityWithUsage(NetworkUsage usage, double data_rate) {
      double max_used_data_rate = band.ChannelWidth * bits_per_symbol - data_rate;
      // Rx bandwidth check.
      if (usage.SpectrumUsage(rx_antenna) * bits_per_symbol > max_used_data_rate) {
        return false;
      }
      return true;
    }

    public double CapacityWithUsage(NetworkUsage usage) {
      // Tx power check.
      double power_data_rate = max_data_rate * (1 - usage.TxPowerUsage(tx_antenna));

      // Bandwidth check.
      double rx_spectrum_usage = usage.SpectrumUsage(rx_antenna);
      double tx_spectrum_usage = usage.SpectrumUsage(tx_antenna);
      double spectrum_usage = (rx_spectrum_usage > tx_spectrum_usage) ? rx_spectrum_usage : tx_spectrum_usage;
      double spectrum_data_rate = (band.ChannelWidth - spectrum_usage) * bits_per_symbol;

      return (power_data_rate > spectrum_data_rate) ? spectrum_data_rate : power_data_rate;
    }

    public double TxPowerUsageFromDataRate(double data_rate) {
      return data_rate / max_data_rate;
    }

    public double SpectrumUsageFromDataRate(double data_rate) {
      return data_rate / bits_per_symbol_;
    }

    private OrientedLink() { }
    private OrientedLink(RACommNode tx,
                         RACommNode rx,
                         RACommLink ra_link,
                         bool forward,
                         Routing routing) {
      Set(tx, rx, ra_link, forward, routing);
    }

    private void Clear() => Set(null, null, null, true, null);
    private void Set(RACommNode tx, RACommNode rx, RACommLink ra_link, bool forward, Routing routing) {
      this.tx = tx;
      this.rx = rx;
      this.ra_link = ra_link;
      this.forward = forward;
      routing_ = routing;
      if (ra_link != null) {
        bits_per_symbol_ = encoder.CodingRate * modulator.ModulationBits;
      }
    }

    public double bits_per_symbol => bits_per_symbol_;
    private double bits_per_symbol_;

    private Routing routing_;
  }

  public enum RoutingMethod {
    DIJKSTRAS, ONEHOP, ASTAR, VGV
  };

  public RoutingMethod method = RoutingMethod.DIJKSTRAS;

  private readonly RoutingNetworkUsage current_network_usage_;

  private readonly Dictionary<(RACommNode, RACommNode), OrientedLink> links_ =
      new Dictionary<(RACommNode, RACommNode), OrientedLink>();

  private readonly Queue<RoutingPrecompute.VGVLink> vgv_links = new Queue<RoutingPrecompute.VGVLink>();
  
  // Stations only capable of transmitting.
  private HashSet<RACommNode> tx_only_ = new HashSet<RACommNode>();
  // Station only capable of receiving.
  private HashSet<RACommNode> rx_only_ = new HashSet<RACommNode>();
  // Station modelled as capable of tracking multiple targets simultaneously,
  // so that each of its antennas really represents multiple independent
  // antennas.  Neither their transmitted power nor their spectrum get used up.
  private HashSet<RACommNode> multiple_tracking_ = new HashSet<RACommNode>();

  public RoutingPrecompute heuristic;
  internal PerRefreshMetric reset_metric = new PerRefreshMetric("Reset");
  internal PerRefreshMetric find_channels_metric = new PerRefreshMetric("Find Channels");
  internal PerRefreshMetric find_channels_duplex_metric = new PerRefreshMetric("Find Channels (Duplex)");
  internal PerRefreshMetric find_channels_ptmp_metric = new PerRefreshMetric("Find Channels (PtMP)");
  internal PerRefreshMetric one_hop_metric = new PerRefreshMetric("One-Hop");
  internal PerRefreshMetric a_star_metric = new PerRefreshMetric("A*");
  internal PerRefreshMetric shortest_path_metric = new PerRefreshMetric("Shortest Path");
  internal PerRefreshMetric dijkstras_metric = new PerRefreshMetric("Dijkstra's");
  internal PerRefreshMetric vgv_routing_metric = new PerRefreshMetric("VGV A*");
  internal PerRefreshMetric vgv_dijkstras_metric = new PerRefreshMetric("VGV Dijkstra's");
  internal static PerRefreshMetric link_usage_metric = new PerRefreshMetric("UseLinks/UseLink");
  internal static PerRefreshMetric fake_usage_metric = new PerRefreshMetric("Fake UseLinks");
}

}
