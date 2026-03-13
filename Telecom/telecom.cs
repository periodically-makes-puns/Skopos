using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RealAntennas;
using RealAntennas.MapUI;
using RealAntennas.Network;
using Contracts;
using ContractConfigurator;

namespace σκοπός {
  [KSPScenario(
    ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames |
    ScenarioCreationOptions.RemoveFromSandboxGames | ScenarioCreationOptions.RemoveFromScienceSandboxGames,
    new[] { GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.FLIGHT, GameScenes.EDITOR })]
  public sealed class Telecom : ScenarioModule, principia.ksp_plugin_adapter.SupervisedWindowRenderer.ISupervisor {

    public event Action LockClearing;
    public event Action WindowsDisposal;
    public event Action WindowsRendering;

    public static void Log(string message,
                           [CallerFilePath] string file = "",
                           [CallerLineNumber] int line = 0) {
      UnityEngine.Debug.Log($"[Σκοπός Telecom]: {message} ({file}:{line})");
    }

    public override void OnAwake() {
      Log($"Scenario Module OnAwake in {HighLogic.LoadedScene}.");
      Instance = this;
      main_window_ = new MainWindow(this);
    }

    public override void OnLoad(ConfigNode node) {
      serialized_network_ = node.GetNode("network") ?? new ConfigNode();
    }

    public override void OnSave(ConfigNode node) {
      base.OnSave(node);
      if (network == null) {
        node.AddNode("network", serialized_network_);
      } else {
        network.Serialize(node.AddNode("network"));
      }
    }

    public void Start() {
      Log("Starting");
      enabled = false;
      GameEvents.CommNet.OnNetworkInitialized.Add(NetworkInitializedNotify);
      GameEvents.CommNet.OnNetworkInitialized.Add(AddPostUpdateHandler);
      GameEvents.Contract.onContractsLoaded.Add(NotifyContractsLoaded);
      GameEvents.Contract.onContractsLoaded.Add(CleanMaintenanceContracts);
      StartCoroutine(CreateNetwork());
      RegisterStaticTimingMetrics();
    }

    public void OnDestroy() {
      Log("Destroying");
      GameEvents.CommNet.OnNetworkInitialized.Remove(NetworkInitializedNotify);
      GameEvents.Contract.onAccepted.Remove(ReloadContractConnections);
      GameEvents.Contract.onFinished.Remove(ReloadContractConnections);    
      GameEvents.Contract.onContractsLoaded.Remove(NotifyContractsLoaded);
      GameEvents.Contract.onContractsLoaded.Remove(CleanMaintenanceContracts);
    }

    private void NotifyContractsLoaded() {
      Log("Received OnContractsLoaded GameEvent notification");
    }

    private void CleanMaintenanceContracts() {
      double now = Planetarium.GetUniversalTime();
      var to_remove = ContractSystem.Instance.ContractsFinished.OfType<ConfiguredContract>()
          .Where(cc => (cc.DateFinished + contract_cleanup_days_ * 86400.0 <= now) &&
          (cc.contractType.name.StartsWith("maintenance_")) &&
          (!cc.contractType.name.StartsWith("maintenance_intermittent_")) && // Experimental contracts do weird things if they're deleted.
          (cc.Agent?.Name == "skopos_telecom_agent")).ToList();
      foreach (ConfiguredContract contract in to_remove) {
        ContractSystem.Instance.ContractsFinished.Remove(contract);
      }
    }

    private void NetworkInitializedNotify() {
      Log("CommNet Network Initialization fired.");
    }

    private void AddPostUpdateHandler() {
      if (RACommNetNetwork.Instance?.CommNet?.OnNetworkPostUpdate is Action) {
        previous_on_network_post_update = RACommNetNetwork.Instance?.CommNet?.OnNetworkPostUpdate;
      }
      if (!(RACommNetNetwork.Instance?.CommNet is null)) {
        RACommNetNetwork.Instance.CommNet.OnNetworkPostUpdate = PostUpdateHandler;
      }
    }

    private void PostUpdateHandler() {
      //const double MIN_UPDATE_INTERVAL = 0.1;
      if (previous_on_network_post_update is Action) previous_on_network_post_update();
      else {
        double now = Planetarium.GetUniversalTime();
        if (last_refresh_ut <= now) {
          do_refresh = true;
        }
      }
    }

    private IEnumerator CreateNetwork() {
      while (RACommNetScenario.RACN == null || !CommNet.CommNetNetwork.Initialized) {
          yield return new UnityEngine.WaitForFixedUpdate();
      }
      Log("Creating Network");
      network = new Network(serialized_network_);
      ReloadContractConnections(null);
      enabled = true;
      GameEvents.Contract.onAccepted.Add(ReloadContractConnections);
      GameEvents.Contract.onFinished.Add(ReloadContractConnections);
      while (network.AllGround().Any(x => x.Comm == null)) {
        Log("Network creation stalling for station CommNetHomes to create...");
        yield return new UnityEngine.WaitForEndOfFrame();
      }
      network.UpdateStationVisibilityHandler();
    }

    internal IEnumerator UpdateGroundStationNode(RACommNetHome station) {
      Log($"Creating GroundStationSiteNode for {station.name}...");
      while (network is null || station.Comm is null || !(RACommNetUI.Instance is RACommNetUI))  {
        yield return new UnityEngine.WaitForEndOfFrame();
      } // Stall for RACommNetHomes.
      (RACommNetUI.Instance as RACommNetUI).ConstructSiteNode(station); 
    }

    private bool on_contracts_changed_cr_running = false;
    internal void ReloadContractConnections(Contracts.Contract data) {
      if (!on_contracts_changed_cr_running) {
        StartCoroutine(DelayedContractReload(data));
      }
    }

    private IEnumerator DelayedContractReload(Contracts.Contract data) {
      on_contracts_changed_cr_running = true;
      yield return new UnityEngine.WaitForFixedUpdate();
      while (!Contracts.ContractSystem.loaded && network != null) {
        yield return new UnityEngine.WaitForEndOfFrame();
      }
      network.ReloadContractConnections();
      on_contracts_changed_cr_running = false;
    }

    private void RegisterStaticTimingMetrics() {
      RegisterRefreshMetric(Service.service_availability_metric);
      RegisterRefreshMetric(Routing.link_usage_metric);
      RegisterRefreshMetric(Routing.fake_usage_metric);
    }

    internal void RegisterRefreshMetric(PerRefreshMetric metric) {
      registered_metrics.Add(metric);
    }

    private void OnGUI() {
      if (!enabled) return;
      if (KSP.UI.Screens.ApplicationLauncher.Ready && toolbar_button_ == null) {
        LoadTextureIfExists(out UnityEngine.Texture toolbar_button_texture,
                            "skopos_telecom.png");
        toolbar_button_ =
            KSP.UI.Screens.ApplicationLauncher.Instance.AddModApplication(
                onTrue          : () => main_window_.Show(),
                onFalse         : () => main_window_.Hide(),
                onHover         : null,
                onHoverOut      : null,
                onEnable        : null,
                onDisable       : null,
                visibleInScenes :
                    KSP.UI.Screens.ApplicationLauncher.AppScenes.ALWAYS &
                    ~KSP.UI.Screens.ApplicationLauncher.AppScenes.VAB &
                    ~KSP.UI.Screens.ApplicationLauncher.AppScenes.SPH,
                texture         : toolbar_button_texture);
      }
      if (HighLogic.LoadedScene == GameScenes.EDITOR) {
        main_window_.Hide();
      }
      // Make sure the state of the toolbar button remains consistent with the
      // state of the window.
      if (main_window_.Shown()) {
        toolbar_button_?.SetTrue(makeCall : false);
      } else {
        toolbar_button_?.SetFalse(makeCall : false);
      }

      if (main_window_.Shown()) {
        WindowsRendering();
      } else {
        LockClearing();
      }
    }

    private void OnDisable() {
      Log("OnDisable");
      if (toolbar_button_ != null) {
        KSP.UI.Screens.ApplicationLauncher.Instance.RemoveModApplication(
            toolbar_button_);
      }
    }

    private bool LoadTextureIfExists(out UnityEngine.Texture texture,
                                     string path) {
      string full_path =
          KSPUtil.ApplicationRootPath + Path.DirectorySeparatorChar +
          "GameData" + Path.DirectorySeparatorChar +
          "Skopos" + Path.DirectorySeparatorChar +
          path;
      if (File.Exists(full_path)) {
        var texture2d = new UnityEngine.Texture2D(2, 2);
        UnityEngine.ImageConversion.LoadImage(
            texture2d,
            File.ReadAllBytes(full_path));
        texture = texture2d;
        return true;
      } else {
        texture = null;
        return false;
      }
    }

    private void FixedUpdate() {
      if (HighLogic.LoadedScene != GameScenes.EDITOR) {
        // Time does not advance in the VAB, but after a revert, it is incorrectly stuck in the past.
        ut_ = Planetarium.GetUniversalTime();
      }
      
      if (do_refresh) {
        foreach (PerRefreshMetric metric in registered_metrics) {
          metric.StartRefresh();
        }
        network?.Refresh();
        last_refresh_ut = ut_;
        do_refresh = false;
      }
    }

    private void LateUpdate() {
      if (!main_window_.show_network) {
        return;
      }
      if (!MapView.MapIsEnabled) {
        return;
      }
      var ui = CommNet.CommNetUI.Instance as RACommNetUI;
      if (ui == null) {
        return;
      }
      HashSet<RACommNode> stations =
          (from station in network.AllGround() select station.Comm).ToHashSet();
      foreach (var station in stations) {
        ui.OverrideShownCones.Add(station);
      }
      foreach (Vessel vessel in FlightGlobals.Vessels) {
        if (vessel?.connection?.Comm is RACommNode node &&
            (main_window_.focused_vessel is null || node == main_window_.focused_vessel)) {
          ui.OverrideShownCones.Add(node);
        }
      }
      foreach (var link in CommNet.CommNetNetwork.Instance.CommNet.Links) {
        if (link.a is RACommNode node_a &&
            (node_a.ParentVessel != null || stations.Contains(node_a)) &&
            link.b is RACommNode node_b &&
            (node_b.ParentVessel != null || stations.Contains(node_b)) &&
            (main_window_.focused_vessel is null || node_a == main_window_.focused_vessel || node_b == main_window_.focused_vessel)) {
          ui.OverrideShownLinks.Add(link);
        }
      }
    }


    public static Telecom Instance { get; private set; }

    public Network network { get; private set; }
    private ConfigNode serialized_network_;
    [KSPField(isPersistant = true)]
    internal MainWindow main_window_;
    public double last_universal_time => ut_;
    [KSPField(isPersistant = true)]
    private double ut_;
    [KSPField(isPersistant = true)]
    internal double max_alert_rate_in_days_ = 0;
    [KSPField(isPersistant = true)]
    internal double contract_cleanup_days_ = 366;
    [KSPField(isPersistant = true)]
    public bool stop_warp_in_sim_ = true;
    [KSPField(isPersistant = true)]
    public Routing.RoutingMethod routing_method_ = Routing.RoutingMethod.DIJKSTRAS;
    private KSP.UI.Screens.ApplicationLauncherButton toolbar_button_;

    internal readonly List<PerRefreshMetric> registered_metrics = new List<PerRefreshMetric>();
    internal bool do_refresh = false;
    private double last_refresh_ut = 0;
    private Action previous_on_network_post_update = null;
  }
}
