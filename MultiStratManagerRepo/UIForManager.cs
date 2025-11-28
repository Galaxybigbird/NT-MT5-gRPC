using System;
using System.Windows.Input;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using System.Windows.Media.Imaging;
using System.Globalization;
using NinjaTrader.NinjaScript;
using System.Windows.Threading; // Added for Dispatcher
using System.ComponentModel; // Added for INotifyPropertyChanged
using System.Threading.Tasks; // Added for Task for async operations

namespace NinjaTrader.NinjaScript.AddOns
{
    public class PnlToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double pnl)
            {
                return pnl >= 0 ? Brushes.Green : Brushes.Red;
            }
            if (value is int pnlInt) // Handle if PNL comes as int
            {
                return pnlInt >= 0 ? Brushes.Green : Brushes.Red;
            }
            // Fallback for cases where conversion might fail or value is not double/int
            return Brushes.Black; // Default color
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    
    public class EnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return null;
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString())) return null;
            
            if (targetType == typeof(TrailingActivationType))
            {
                return Enum.Parse(typeof(TrailingActivationType), value.ToString());
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Display information for active trailing stops
    /// </summary>
    public class TrailingStopDisplayInfo : INotifyPropertyChanged
    {
        public string BaseId { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public double EntryPrice { get; set; }
        public double CurrentPrice { get; set; }
        public double StopLevel { get; set; }
        public double UnrealizedPnL { get; set; }
        public double StopDistancePoints { get; set; }
        public double StopDistancePercent { get; set; }
        public string Status { get; set; }
        public DateTime ActivationTime { get; set; }
        public int UpdateCount { get; set; }
        public double MaxProfit { get; set; }
        
        public SolidColorBrush PnLBrush
        {
            get { return UnrealizedPnL >= 0 ? Brushes.Green : Brushes.Red; }
        }
        
        public SolidColorBrush StopDistanceBrush
        {
            get
            {
                if (StopDistancePercent <= 1.0) return Brushes.Red;      // Very close to stop
                if (StopDistancePercent <= 3.0) return Brushes.Orange;   // Close to stop
                return Brushes.LightGreen;                                // Safe distance
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// User interface window for the Multi-Strategy Manager
    /// </summary>
    public class UIForManager : NTWindow
    {
        private ComboBox accountComboBox;
        private TextBlock realizedBalanceText;
        private TextBlock unrealizedBalanceText;
        private TextBlock totalPnlText; // Added for Total PnL
        private ToggleButton enabledToggle;
        private Button resetDailyStatusButton; // Added for resetting daily limit status
        private Button flushExposureButton;
        private Account selectedAccount;
        private TextBox dailyTakeProfitInput;
        private TextBox dailyLossLimitInput;
        private DataGrid strategyGrid;
        private double dailyTakeProfit;
        private double dailyLossLimit;
        private System.Collections.ObjectModel.ObservableCollection<StrategyDisplayInfo> activeStrategies;
        // Key: Account Name, Value: HashSet of unique strategy system names the user has explicitly interacted with (checked "Enabled")
        private Dictionary<string, HashSet<string>> explicitlyManagedStrategySystemNamesByAccount = new Dictionary<string, HashSet<string>>();
        private DateTime lastResetDate;
        private System.Windows.Threading.DispatcherTimer strategyStatePollTimer;
        private bool dailyLimitHitForSelectedAccountToday = false; // Flag to track if daily P&L limit is hit
        private string grpcServerAddress = "http://localhost:50051"; // gRPC server address (replaces HTTP URL)
        private TextBox grpcUrlInput; // gRPC server address input
        private Button pingBridgeButton; // Added for Ping Bridge button
        private CheckBox enableSLTPRemovalCheckBox;
        private TextBox sltpRemovalDelayTextBox;
        private DataGrid trailingStopsGrid;
        private System.Collections.ObjectModel.ObservableCollection<TrailingStopDisplayInfo> activeTrailingStops;
        
        public string GrpcServerAddress { get { return grpcServerAddress; } }

        /// <summary>
        /// Represents information about a trading strategy
        /// </summary>
        public class StrategyInfo
        {
            private string strategy;
            private string accountDisplayName;
            private string instrument;
            private string dataSeries;
            private string parameter;
            private string position;
            private string accountPosition;
            private string sync;
            private double averagePrice;
            private double unrealizedPL;
            private double realizedPL;
            private string connection;
            private bool enabled;

            /// <summary>
            /// Gets or sets the strategy name
            /// </summary>
            public string Strategy { get { return strategy; } set { strategy = value; } }
            
            /// <summary>
            /// Gets or sets the account display name
            /// </summary>
            public string AccountDisplayName { get { return accountDisplayName; } set { accountDisplayName = value; } }
            
            /// <summary>
            /// Gets or sets the instrument name
            /// </summary>
            public string Instrument { get { return instrument; } set { instrument = value; } }
            
            /// <summary>
            /// Gets or sets the data series information
            /// </summary>
            public string DataSeries { get { return dataSeries; } set { dataSeries = value; } }
            
            /// <summary>
            /// Gets or sets the parameter information
            /// </summary>
            public string Parameter { get { return parameter; } set { parameter = value; } }
            
            /// <summary>
            /// Gets or sets the position information
            /// </summary>
            public string Position { get { return position; } set { position = value; } }
            
            /// <summary>
            /// Gets or sets the account position information
            /// </summary>
            public string AccountPosition { get { return accountPosition; } set { accountPosition = value; } }
            
            /// <summary>
            /// Gets or sets the sync status
            /// </summary>
            public string Sync { get { return sync; } set { sync = value; } }
            
            /// <summary>
            /// Gets or sets the average price
            /// </summary>
            public double AveragePrice { get { return averagePrice; } set { averagePrice = value; } }
            
            /// <summary>
            /// Gets or sets the unrealized profit/loss
            /// </summary>
            public double UnrealizedPL { get { return unrealizedPL; } set { unrealizedPL = value; } }
            
            /// <summary>
            /// Gets or sets the realized profit/loss
            /// </summary>
            public double RealizedPL { get { return realizedPL; } set { realizedPL = value; } }
            
            /// <summary>
            /// Gets or sets the connection information
            /// </summary>
            public string Connection { get { return connection; } set { connection = value; } }
            
            /// <summary>
            /// Gets or sets whether the strategy is enabled
            /// </summary>
            public bool Enabled { get { return enabled; } set { enabled = value; } }
        }

        /// <summary>
        /// Represents data for display in the strategy grid
        /// </summary>
        public class StrategyDisplayInfo : INotifyPropertyChanged
        {
            // INotifyPropertyChanged implementation
            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

            // Backing fields
            private string _strategyName;
            private string _accountDisplayName;
            private string _instrumentName;
            private string _dataSeriesInfo;
            private string _parameters;
            private string _strategyPosition;
            private int _accountPosition;
            private string _syncStatus;
            private double _averagePrice;
            private double _unrealizedPL;
            private double _realizedPL;
            private bool _isEnabled;
            private string _connectionStatus;

            /// <summary>
            /// Gets or sets the strategy name
            /// </summary>
            public string StrategyName
            {
                get { return _strategyName; }
                set { if (_strategyName != value) { _strategyName = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the account display name
            /// </summary>
            public string AccountDisplayName
            {
                get { return _accountDisplayName; }
                set { if (_accountDisplayName != value) { _accountDisplayName = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the instrument name
            /// </summary>
            public string InstrumentName
            {
                get { return _instrumentName; }
                set { if (_instrumentName != value) { _instrumentName = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the data series information
            /// </summary>
            public string DataSeriesInfo
            {
                get { return _dataSeriesInfo; }
                set { if (_dataSeriesInfo != value) { _dataSeriesInfo = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the parameter information
            /// </summary>
            public string Parameters
            {
                get { return _parameters; }
                set { if (_parameters != value) { _parameters = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the strategy position (e.g., "Flat", "Long", "Short")
            /// </summary>
            public string StrategyPosition
            {
                get { return _strategyPosition; }
                set { if (_strategyPosition != value) { _strategyPosition = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the account position size
            /// </summary>
            public int AccountPosition
            {
                get { return _accountPosition; }
                set { if (_accountPosition != value) { _accountPosition = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the sync status (e.g., "Synced", "Not Synced")
            /// </summary>
            public string SyncStatus
            {
                get { return _syncStatus; }
                set { if (_syncStatus != value) { _syncStatus = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the average price
            /// </summary>
            public double AveragePrice
            {
                get { return _averagePrice; }
                set { if (_averagePrice != value) { _averagePrice = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the unrealized profit/loss
            /// </summary>
            public double UnrealizedPL
            {
                get { return _unrealizedPL; }
                set { if (_unrealizedPL != value) { _unrealizedPL = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the realized profit/loss
            /// </summary>
            public double RealizedPL
            {
                get { return _realizedPL; }
                set { if (_realizedPL != value) { _realizedPL = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets whether the strategy is enabled
            /// </summary>
            public bool IsEnabled
            {
                get { return _isEnabled; }
                set
                {
                    if (_isEnabled != value)
                    {
                        _isEnabled = value;
                        OnPropertyChanged(); // Automatically uses "IsEnabled"
                    }
                }
            }

            /// <summary>
            /// Gets or sets the connection status
            /// </summary>
            public string ConnectionStatus
            {
                get { return _connectionStatus; }
                set { if (_connectionStatus != value) { _connectionStatus = value; OnPropertyChanged(); } }
            }

            /// <summary>
            /// Gets or sets the reference to the underlying StrategyBase object.
            /// This property does NOT need change notification.
            /// </summary>
            public StrategyBase StrategyReference { get; set; }
        }

        /// <summary>
        /// Initializes a new instance of the UIForManager class
        /// </summary>
        public UIForManager()
        {
            try
            {
                MultiStratManager.Instance?.LogDebug("UI", "UIForManager constructor started");

                // ✅ RECOMPILATION SAFETY: Prevent multiple UI instances
                if (strategyStatePollTimer != null)
                {
                    MultiStratManager.Instance?.LogDebug("UI", "Disposing existing timer from previous instance");
                    try
                    {
                        strategyStatePollTimer.Stop();
                        strategyStatePollTimer.Tick -= StrategyStatePollTimer_Tick;
                        strategyStatePollTimer = null;
                    }
                    catch { }
                }

                // Add PnL to Brush Converter to resources
                if (!this.Resources.Contains("PnlColorConverter"))
                {
                    this.Resources.Add("PnlColorConverter", new PnlToBrushConverter());
                }
                
                // Load custom styles - Fix the resource loading path
                // Programmatically apply styles instead of loading from XAML
                ApplyProgrammaticStyles();
                
                // Ensure we're on the UI thread
                if (!CheckAccess())
                {
                    MultiStratManager.Instance?.LogError("UI", "ERROR: UIForManager constructor called from non-UI thread. UI must be created on the UI thread.");
                    throw new InvalidOperationException("The UIForManager must be created on the UI thread.");
                }
                
                // Set window properties
                Title = "Multi-Strategy Manager";
                Width = 1200;
                Height = 800;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Style = Resources["ModernWindowStyle"] as Style;
                MinWidth = 800;
                MinHeight = 600;
                
                // Try to set icon, but handle possible exceptions
                try
                {
                    Icon = Application.Current.MainWindow.Icon;
                }
                catch
                {
                    // Non-critical if icon fails
                }
                
                // Initialize data
                dailyTakeProfit = 1000;
                dailyLossLimit = 500;
                lastResetDate = DateTime.Today;
                activeStrategies = new System.Collections.ObjectModel.ObservableCollection<StrategyDisplayInfo>();
                activeTrailingStops = new System.Collections.ObjectModel.ObservableCollection<TrailingStopDisplayInfo>();
                
                // Create UI
                MultiStratManager.Instance?.LogDebug("UI", "Creating UI");
                CreateUI();
                
                // Register for loaded event to ensure proper initialization
                this.Loaded += new RoutedEventHandler(OnWindowLoaded);
                // Register for closed event for cleanup
                this.Closed += new EventHandler(OnWindowClosed);

                // Initialize and configure the polling timer
                strategyStatePollTimer = new System.Windows.Threading.DispatcherTimer();
                strategyStatePollTimer.Interval = TimeSpan.FromMilliseconds(500); // Poll every 500ms
                strategyStatePollTimer.Tick += StrategyStatePollTimer_Tick;

                MultiStratManager.Instance?.LogInfo("UI", "UIForManager constructor completed successfully");
            }
            catch (Exception ex)
            {
                MultiStratManager.Instance?.LogError("UI", $"ERROR in UIForManager constructor: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Helper method to log messages to bridge dashboard via MultiStratManager
        /// </summary>
        private void LogToBridge(string level, string category, string message)
        {
            try
            {
                if (MultiStratManager.Instance != null)
                {
                    switch (level.ToUpper())
                    {
                        case "ERROR":
                            MultiStratManager.Instance.LogError(category, message);
                            break;
                        case "WARN":
                            MultiStratManager.Instance.LogWarn(category, message);
                            break;
                        case "DEBUG":
                            MultiStratManager.Instance.LogDebug(category, message);
                            break;
                        case "INFO":
                        default:
                            MultiStratManager.Instance.LogInfo(category, message);
                            break;
                    }
                }
                else
                {
                    // Fallback to NT terminal if MultiStratManager not available
                    NinjaTrader.Code.Output.Process($"[UI][{level}][{category}] {message}", PrintTo.OutputTab1);
                }
            }
            catch (Exception ex)
            {
                // Last resort fallback
                NinjaTrader.Code.Output.Process($"[UI] Logging failed: {ex.Message} | Original: [{level}][{category}] {message}", PrintTo.OutputTab1);
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update account list
                UpdateAccountList();
                
                // Initialize empty strategy list
                if (activeStrategies == null)
                {
                    activeStrategies = new System.Collections.ObjectModel.ObservableCollection<StrategyDisplayInfo>();
                }
                else
                {
                    // Unregister old strategies before clearing
                    foreach (var stratInfo in activeStrategies)
                    {
                        if (stratInfo.StrategyReference != null)
                            MultiStratManager.UnregisterStrategyForMonitoring(stratInfo.StrategyReference);
                    }
                    activeStrategies.Clear();
                }
                
                // Initialize the grid with an empty list - no sample data
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    if (strategyGrid != null)
                    {
                        LogToBridge("DEBUG", "UI", "Initializing empty grid - no sample data");
                        strategyGrid.ItemsSource = activeStrategies;
                        strategyGrid.UpdateLayout();
                    }
                    else
                    {
                        LogToBridge("ERROR", "UI", "ERROR: strategyGrid is null");
                    }
                    
                    // Force layout update - critical for correct display
                    UpdateLayout();
// Populate the grid initially
                    UpdateStrategyGrid(accountComboBox.SelectedItem as Account);
                }));
                
                // Start tracking account updates
                StartBalanceTracking();
                
                // Force initial update of P&L display
                if (selectedAccount != null)
                {
                    LogToBridge("DEBUG", "UI", $"[UIForManager] OnWindowLoaded: Initial P&L update for account {selectedAccount.Name}");
                    
                    // Ensure we're subscribed to this specific account's events
                    selectedAccount.AccountItemUpdate += OnAccountUpdateHandler;
                    
                    // Force an immediate update of the P&L display
                    UpdateBalanceDisplay();
                }
                
                // Make sure toggle is in "Disabled" state initially
                if (enabledToggle != null)
                {
                    enabledToggle.IsChecked = false;
                }

                // Start the polling timer
                strategyStatePollTimer.Start();
                LogToBridge("INFO", "UI", "Strategy state polling timer started.");

                // Initial setup for MultiStratManager instance
                if (MultiStratManager.Instance != null)
                {
                    this.DataContext = MultiStratManager.Instance; // Set DataContext for PnL bindings
                    LogToBridge("DEBUG", "UI", "[UIForManager] OnWindowLoaded: DataContext set to MultiStratManager.Instance.");
                    LogToBridge("DEBUG", "UI", "[UIForManager] OnWindowLoaded: Setting initial Bridge URL and Monitored Account in MultiStratManager.");
                    MultiStratManager.Instance.SetGrpcAddress(this.GrpcServerAddress);
                    MultiStratManager.Instance.SetMonitoredAccount(this.selectedAccount);
 
                    // Subscribe to the PingReceivedFromBridge event
                    MultiStratManager.Instance.PingReceivedFromBridge += MultiStratManager_PingReceivedFromBridge;
                    LogToBridge("DEBUG", "UI", "[UIForManager] Subscribed to PingReceivedFromBridge event.");
                }
                
                LogToBridge("INFO", "UI", "Window loaded successfully. Toggle the 'Enabled' button to activate strategy tracking.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in OnWindowLoaded: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }

        private void StrategyStatePollTimer_Tick(object sender, EventArgs e)
        {
            // ✅ FIX: Add safety check to prevent timer from running during cleanup
            if (strategyStatePollTimer == null)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (strategyGrid == null || activeStrategies == null || strategyStatePollTimer == null)
                        return;

                // Original strategy state polling logic
                foreach (StrategyDisplayInfo stratInfo in activeStrategies)
                {
                    if (stratInfo.StrategyReference != null)
                    {
                        try
                        {
                            // Always update IsEnabled to reflect the actual strategy state.
                            // The dailyLimitHitForSelectedAccountToday flag will prevent new orders and disable strategies,
                            // but the checkbox should still reflect the true current state of the strategy.
                            State currentState = stratInfo.StrategyReference.State;
                            bool shouldBeEnabled = currentState == State.Active || currentState == State.Realtime;

                            if (stratInfo.IsEnabled != shouldBeEnabled)
                            {
                                stratInfo.IsEnabled = shouldBeEnabled;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogToBridge("ERROR", "UI", $"[UIForManager] Error polling state for {stratInfo.StrategyName}: {ex.Message}");
                        }
                    }
                }
                
                // Update trailing stops display
                try
                {
                    UpdateTrailingStopsDisplay();
                }
                catch (Exception ex)
                {
                    LogToBridge("ERROR", "UI", $"[UIForManager] Error in UpdateTrailingStopsDisplay: {ex.Message}");
                }

                // P&L Monitoring and Limit Checking Logic
                if (enabledToggle != null && enabledToggle.IsChecked == true)
                {
                    // Daily Reset Logic
                    if (DateTime.Today != lastResetDate)
                    {
                        LogToBridge("INFO", "SYSTEM", $"[UIForManager] New day detected. Resetting daily P&L limit flag. Old date: {lastResetDate}, New date: {DateTime.Today}");
                        dailyLimitHitForSelectedAccountToday = false;
                        lastResetDate = DateTime.Today;
                        // If the toggle was programmatically set to "Limit Reached", reset it.
                        if (enabledToggle.Content.ToString() == "Limit Reached")
                        {
                            enabledToggle.Content = "Enabled"; // Or back to "Disabled" if it should be unchecked
                        }
                    }

                    if (dailyLimitHitForSelectedAccountToday)
                    {
                        // Daily P&L limit already hit for the selected account today. Skipping further checks.
                        return; // Don't proceed if limit is already hit for the day
                    }

                    if (selectedAccount == null)
                    {
                        // P&L Check: No account selected.
                        return;
                    }

                    try
                    {
                        // Get the latest P&L values directly from the account
                        // Using explicit decimal cast to maintain precision in financial calculations
                        decimal currentDailyUnrealizedPnL = (decimal)(selectedAccount.GetAccountItem(AccountItem.UnrealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0);
                        decimal currentDailyRealizedPnL = (decimal)(selectedAccount.GetAccountItem(AccountItem.RealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0);
                        decimal currentTotalDailyPnL = currentDailyUnrealizedPnL + currentDailyRealizedPnL;
                        
                        // Format values for logging with "C" for currency format
                        LogToBridge("DEBUG", "PNL", $"[UIForManager] P&L Check for account {selectedAccount.Name}: Current Total Daily P&L (Unrealized + Realized) = {currentTotalDailyPnL.ToString("C")} (Unrealized: {currentDailyUnrealizedPnL.ToString("C")}, Realized: {currentDailyRealizedPnL.ToString("C")}), Take Profit Limit = {dailyTakeProfit.ToString("C")}, Loss Limit = {dailyLossLimit.ToString("C")}");

                        bool limitHit = false;
                        string limitTypeHit = string.Empty;

                        // Check Take Profit Limit - ensure we have positive P&L >= take profit limit
                        if (dailyTakeProfit > 0 && currentTotalDailyPnL >= (decimal)dailyTakeProfit)
                        {
                            limitHit = true;
                            limitTypeHit = "Take Profit";
                            LogToBridge("WARN", "PNL", $"[UIForManager] TAKE PROFIT LIMIT HIT for account {selectedAccount.Name}. Total P&L: {currentTotalDailyPnL.ToString("C")}, Limit: {dailyTakeProfit.ToString("C")}");
                        }
                        // Check Daily Loss Limit - ensure we have negative P&L <= negative loss limit
                        else if (dailyLossLimit > 0 && currentTotalDailyPnL <= (-1 * (decimal)dailyLossLimit))
                        {
                            limitHit = true;
                            limitTypeHit = "Loss Limit";
                            LogToBridge("WARN", "PNL", $"[UIForManager] LOSS LIMIT HIT for account {selectedAccount.Name}. Total P&L: {currentTotalDailyPnL.ToString("C")}, Limit: {(-1 * (decimal)dailyLossLimit).ToString("C")}");
                        }

                        if (limitHit)
                        {
                            dailyLimitHitForSelectedAccountToday = true;
                            LogToBridge("WARN", "PNL", $"[UIForManager] Daily {limitTypeHit} limit hit for account {selectedAccount.Name}. Flattening all positions and disabling strategies for this account today.");

                            // Update toggle button text and state
                            if (enabledToggle != null)
                            {
                                enabledToggle.Content = "Limit Reached";
                                // Optionally, uncheck the toggle if it should visually represent "disabled due to limit"
                                // enabledToggle.IsChecked = false; // This might conflict with user's explicit enable/disable
                            }

                            // Flatten all positions for the account first
                            try
                            {
                                LogToBridge("INFO", "TRADING", $"[UIForManager] Attempting to flatten all positions for account {selectedAccount.Name}.");
                                if (selectedAccount != null && selectedAccount.Positions != null)
                                {
                                    // Create a list of positions to avoid issues if the collection is modified during iteration.
                                    var positionsToFlatten = new System.Collections.Generic.List<NinjaTrader.Cbi.Position>(selectedAccount.Positions);
                                    
                                    if (positionsToFlatten.Count == 0)
                                    {
                                        LogToBridge("INFO", "TRADING", $"[UIForManager] No open positions to flatten for account {selectedAccount.Name}.");
                                    }
                                    else
                                    {
                                        LogToBridge("INFO", "TRADING", $"[UIForManager] Found {positionsToFlatten.Count} positions to potentially flatten for account {selectedAccount.Name}.");
                                        // Outer try-catch for the entire position iteration loop
                                        try
                                        {
                                            foreach (NinjaTrader.Cbi.Position position in positionsToFlatten)
                                            {
                                                try // Inner try-catch for each position
                                                {
                                                    // Ensure position is not null and actually has a market position (is not flat)
                                                    if (position != null && position.MarketPosition != NinjaTrader.Cbi.MarketPosition.Flat)
                                                    {
                                                        LogToBridge("DEBUG", "TRADING", $"[UIForManager] Processing position to flatten: Instrument={position.Instrument.FullName}, MarketPosition={position.MarketPosition}, Quantity={position.Quantity}, Account={selectedAccount.Name}");

                                                        NinjaTrader.Cbi.OrderAction actionToFlatten;
                                                        if (position.MarketPosition == NinjaTrader.Cbi.MarketPosition.Long)
                                                            actionToFlatten = NinjaTrader.Cbi.OrderAction.Sell;
                                                        else // Position must be Short if not Flat (checked in outer if) and not Long
                                                            actionToFlatten = NinjaTrader.Cbi.OrderAction.Buy;

                                                        // Create and submit an Order object as UIForManager is not a NinjaScript
                                                        NinjaTrader.Cbi.Order orderToFlatten = new NinjaTrader.Cbi.Order
                                                        {
                                                            Account = selectedAccount,
                                                            Instrument = position.Instrument,
                                                            OrderAction = actionToFlatten,
                                                            OrderType = NinjaTrader.Cbi.OrderType.Market,
                                                            Quantity = (int)position.Quantity,
                                                            LimitPrice = 0, // Not strictly necessary for Market orders
                                                            StopPrice = 0,  // Not strictly necessary for Market orders
                                                            TimeInForce = NinjaTrader.Cbi.TimeInForce.Day, // Default for market flatten
                                                            Name = "PnLLimitFlatten"
                                                            // Oco = string.Empty, // OCO is typically handled differently if needed
                                                        };
                                                        LogToBridge("DEBUG", "TRADING", $"[UIForManager] Creating flattening order: Account={orderToFlatten.Account.Name}, Instrument={orderToFlatten.Instrument.FullName}, Action={orderToFlatten.OrderAction}, Type={orderToFlatten.OrderType}, Quantity={orderToFlatten.Quantity}, TimeInForce={orderToFlatten.TimeInForce}, Name={orderToFlatten.Name}");

                                                        LogToBridge("DEBUG", "TRADING", $"[UIForManager] Attempting to submit flattening order list for {position.Instrument.FullName} via selectedAccount.Submit().");
                                                        selectedAccount.Submit(new List<NinjaTrader.Cbi.Order> { orderToFlatten });
                                                        LogToBridge("INFO", "TRADING", $"[UIForManager] Successfully submitted flattening order list for {position.Instrument.FullName}.");
                                                    }
                                                    else if (position != null && position.MarketPosition == NinjaTrader.Cbi.MarketPosition.Flat)
                                                    {
                                                        LogToBridge("DEBUG", "TRADING", $"[UIForManager] Position {position.Instrument.FullName} in account {selectedAccount.Name} is already flat.");
                                                    }
                                                }
                                                catch (Exception ex_inner)
                                                {
                                                    LogToBridge("ERROR", "TRADING", $"ERROR: [UIForManager] ERROR flattening position {position?.Instrument?.FullName ?? "Unknown Instrument"} ({position?.MarketPosition.ToString() ?? "N/A"} {position?.Quantity.ToString() ?? "N/A"}): {ex_inner.Message} | StackTrace: {ex_inner.StackTrace} | InnerException: {ex_inner.InnerException?.Message}");
                                                }
                                            } // End foreach
                                            LogToBridge("INFO", "TRADING", $"[UIForManager] Finished processing positions for flattening in account {selectedAccount.Name}.");
                                        }
                                        catch (Exception ex_outer_loop)
                                        {
                                            LogToBridge("ERROR", "TRADING", $"ERROR: [UIForManager] ERROR in position flattening loop: {ex_outer_loop.Message} | StackTrace: {ex_outer_loop.StackTrace} | InnerException: {ex_outer_loop.InnerException?.Message}");
                                        }
                                    }
                                }
                                else
                                {
                                    LogToBridge("ERROR", "TRADING", $"[UIForManager] Account {selectedAccount?.Name} is null or its Positions collection is null. Cannot flatten.");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogToBridge("ERROR", "TRADING", $"[UIForManager] Error calling FlattenEverything for account {selectedAccount.Name}: {ex.Message}");
                            }
                            
                            // Then, disable all strategies associated with this account
                            foreach (StrategyDisplayInfo stratInfo in activeStrategies.Where(s => s.AccountDisplayName == selectedAccount.DisplayName && s.StrategyReference != null))
                            {
                                try
                                {
                                    if (stratInfo.StrategyReference.State == State.Active || stratInfo.StrategyReference.State == State.Realtime)
                                    {
                                        LogToBridge("INFO", "SYSTEM", $"[UIForManager] Disabling strategy {stratInfo.StrategyName} (due to daily P&L {limitTypeHit} limit).");
                                        
                                        // Position closing is now handled by FlattenEverything() globally.
                                        // Individual stratInfo.StrategyReference.CloseStrategy() is removed.
                                        
                                        // Disable the strategy
                                        stratInfo.StrategyReference.SetState(State.Terminated);
                                        stratInfo.IsEnabled = false; // Update UI
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogToBridge("ERROR", "SYSTEM", $"[UIForManager] Error disabling strategy {stratInfo.StrategyName} (post-flatten attempt): {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogToBridge("ERROR", "PNL", $"[UIForManager] Error in P&L check: {ex.Message}\n{ex.StackTrace}");
                    }
                }
                }
                catch (Exception timerEx)
                {
                    LogToBridge("ERROR", "UI", $"[UIForManager] Error in timer tick: {timerEx.Message}");
                    // Don't rethrow - let timer continue
                }
            }));
        }

        private void UpdateStrategyGrid(Account selectedAccount)
        {
            try
            {
                LogToBridge("DEBUG", "UI", $"[UIForManager] UpdateStrategyGrid called for account: {(selectedAccount != null ? selectedAccount.Name : "null")}");

                if (activeStrategies == null)
                {
                    activeStrategies = new System.Collections.ObjectModel.ObservableCollection<StrategyDisplayInfo>();
                    if (strategyGrid != null) strategyGrid.ItemsSource = activeStrategies;
                }

                // Create a temporary list of new items for the grid
                var newGridDisplayItems = new List<StrategyDisplayInfo>();
                // Keep track of strategy references that are currently live and should be monitored
                var liveReferencesToMonitor = new HashSet<StrategyBase>();

                if (selectedAccount == null)
                {
                    // If no account is selected, clear everything
                    LogToBridge("DEBUG", "UI", "[UIForManager] No account selected. Clearing strategy grid and unregistering all.");
                }
                else
                {
                    // Ensure the account has an entry in our tracking dictionary
                    if (!explicitlyManagedStrategySystemNamesByAccount.ContainsKey(selectedAccount.Name))
                    {
                        explicitlyManagedStrategySystemNamesByAccount[selectedAccount.Name] = new HashSet<string>();
                    }
                    HashSet<string> managedSystemNamesForAccount = explicitlyManagedStrategySystemNamesByAccount[selectedAccount.Name];

                    // Create a map of currently live strategies from selectedAccount.Strategies for quick lookup
                    // The key is the strategy's system name (StrategyBase.Name)
                    Dictionary<string, StrategyBase> liveStrategiesMap = new Dictionary<string, StrategyBase>();
                    if (selectedAccount.Strategies != null)
                    {
                        foreach (var sb in selectedAccount.Strategies)
                        {
                            if (sb != null && !string.IsNullOrEmpty(sb.Name))
                            {
                                liveStrategiesMap[sb.Name] = sb;
                                // Auto-add any live strategy to the managed list if it's not already there.
                                // This makes strategies visible if they are started outside this UI.
                                if (managedSystemNamesForAccount.Add(sb.Name))
                                {
                                    LogToBridge("INFO", "SYSTEM", $"[UIForManager] Discovered and added live strategy '{sb.Name}' to managed list for account '{selectedAccount.Name}'.");
                                }
                            }
                        }
                        LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Found {liveStrategiesMap.Count} live strategies in selectedAccount.Strategies for {selectedAccount.Name}.");
                    }
                    else
                    {
                        LogToBridge("WARN", "SYSTEM", $"[UIForManager] selectedAccount.Strategies is null for {selectedAccount.Name}.");
                    }

                    LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Processing {managedSystemNamesForAccount.Count} explicitly managed strategy names for account {selectedAccount.Name}: {string.Join(", ", managedSystemNamesForAccount)}");

                    foreach (string systemName in managedSystemNamesForAccount)
                    {
                        StrategyBase liveStrategyInstance = null;
                        liveStrategiesMap.TryGetValue(systemName, out liveStrategyInstance);

                        if (liveStrategyInstance != null)
                        {
                            // Strategy is in our managed list AND is currently live
                            string displayName = liveStrategyInstance.Name ?? "Unnamed Strategy"; // Should be same as systemName

                            string instrumentName = "N/A";
                            if (liveStrategyInstance.Instrument != null)
                                instrumentName = liveStrategyInstance.Instrument.FullName;

                            string dataSeriesInfo = "N/A";
                            if (liveStrategyInstance.BarsArray != null && liveStrategyInstance.BarsArray.Length > 0 && liveStrategyInstance.BarsArray[0] != null)
                                dataSeriesInfo = liveStrategyInstance.BarsArray[0].ToString();
                            
                            string parameters = "N/A"; // Placeholder - True parameters would require deeper inspection or different API

                            string strategyPositionString = "N/A";
                            int accountPositionQty = 0;
                            double averagePriceVal = 0.0;

                            if (liveStrategyInstance.Position != null)
                            {
                                strategyPositionString = liveStrategyInstance.Position.MarketPosition.ToString(); // E.g., Long, Short, Flat
                                accountPositionQty = liveStrategyInstance.Position.Quantity;
                                averagePriceVal = liveStrategyInstance.Position.AveragePrice;
                            }

                            double unrealizedPLVal = 0.0;
                            if (liveStrategyInstance.PositionAccount != null && liveStrategyInstance.Instrument?.MarketData?.Last != null)
                            {
                                double lastPrice = liveStrategyInstance.Instrument.MarketData.Last.Price;
                                // It's possible for lastPrice to be 0 if market data isn't fully loaded or for certain instruments.
                                // GetUnrealizedProfitLoss should ideally handle this, but defensive check is fine.
                                if (liveStrategyInstance.Instrument.MasterInstrument.TickSize > 0) // Basic check if instrument is somewhat valid
                                {
                                   unrealizedPLVal = liveStrategyInstance.PositionAccount.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastPrice);
                                }
                            }
                            else if (liveStrategyInstance.PositionAccount != null)
                            {
                                LogToBridge("WARN", "SYSTEM", $"[UIForManager] Warning: PositionAccount exists but Instrument or MarketData is null/incomplete for {displayName}. Cannot calculate Unrealized P/L accurately.");
                            }


                            double realizedPLVal = 0.0;
                            if (liveStrategyInstance.SystemPerformance != null &&
                                liveStrategyInstance.SystemPerformance.AllTrades != null &&
                                liveStrategyInstance.SystemPerformance.AllTrades.TradesPerformance != null) // Currency is a struct, should not be null if TradesPerformance isn't.
                            {
                                realizedPLVal = liveStrategyInstance.SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                            }

                            bool isEnabledState = liveStrategyInstance.State == State.Active || liveStrategyInstance.State == State.Realtime;
                            string currentStrategyState = liveStrategyInstance.State.ToString();

                            newGridDisplayItems.Add(new StrategyDisplayInfo
                            {
                                StrategyName = displayName,
                                AccountDisplayName = selectedAccount.DisplayName,
                                InstrumentName = instrumentName,
                                DataSeriesInfo = dataSeriesInfo,
                                Parameters = parameters,
                                StrategyPosition = strategyPositionString,
                                AccountPosition = accountPositionQty,
                                SyncStatus = "N/A", // Placeholder
                                AveragePrice = averagePriceVal,
                                UnrealizedPL = unrealizedPLVal,
                                RealizedPL = realizedPLVal,
                                IsEnabled = isEnabledState, // Reflects actual strategy state
                                ConnectionStatus = currentStrategyState,
                                StrategyReference = liveStrategyInstance
                            });
                            liveReferencesToMonitor.Add(liveStrategyInstance);
                            LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Added LIVE strategy to display: {displayName}, Enabled: {isEnabledState}, State: {currentStrategyState}, UPL: {unrealizedPLVal}, RPL: {realizedPLVal}");
                        }
                        else
                        {
                            // Strategy is in our managed list BUT is NOT currently live
                            newGridDisplayItems.Add(new StrategyDisplayInfo
                            {
                                StrategyName = systemName, // System name
                                AccountDisplayName = selectedAccount.DisplayName,
                                InstrumentName = "N/A",
                                DataSeriesInfo = "N/A",
                                Parameters = "N/A",
                                StrategyPosition = "N/A",
                                AccountPosition = 0,
                                SyncStatus = "N/A",
                                AveragePrice = 0,
                                UnrealizedPL = 0,
                                RealizedPL = 0,
                                IsEnabled = false, // Not live, so cannot be enabled through UI click here
                                ConnectionStatus = "Not Active/Found",
                                StrategyReference = null // No live reference
                            });
                            LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Added placeholder for MANAGED (not live) strategy: {systemName}");
                        }
                    }
                }

                // Unregister strategies that were previously monitored but are no longer live or relevant
                foreach (var existingStratInfo in activeStrategies.ToList()) // Iterate copy for safe removal
                {
                    if (existingStratInfo.StrategyReference != null && !liveReferencesToMonitor.Contains(existingStratInfo.StrategyReference))
                    {
                        MultiStratManager.UnregisterStrategyForMonitoring(existingStratInfo.StrategyReference);
                        LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Unregistered {existingStratInfo.StrategyName} (was {existingStratInfo.ConnectionStatus}) as it's no longer in the live/monitored set.");
                    }
                }

                // Update the actual DataGrid ItemsSource
                activeStrategies.Clear();
                foreach (var item in newGridDisplayItems.OrderBy(s => s.StrategyName)) // Keep a consistent order
                {
                    activeStrategies.Add(item);
                }

                // Register all current live references
                foreach (var liveRef in liveReferencesToMonitor)
                {
                    MultiStratManager.RegisterStrategyForMonitoring(liveRef);
                }
                
                if (strategyGrid != null) strategyGrid.Items.Refresh();
                LogToBridge("DEBUG", "UI", $"[UIForManager] Strategy grid refreshed. Displaying {activeStrategies.Count} items. Monitoring {liveReferencesToMonitor.Count} live strategies.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"[UIForManager] ERROR in UpdateStrategyGrid: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CreateUI()
        {
            try
            {
                LogToBridge("DEBUG", "UI", "Creating UI elements");

                // Create main grid
                Grid mainGrid = new Grid();
                RowDefinition rowDef1 = new RowDefinition(); // Header
                rowDef1.Height = GridLength.Auto;
                mainGrid.RowDefinitions.Add(rowDef1);

                RowDefinition rowDef2 = new RowDefinition(); // TabControl
                rowDef2.Height = new GridLength(1, GridUnitType.Star);
                mainGrid.RowDefinitions.Add(rowDef2);

                // Create header
                Border headerBorder = new Border();
                headerBorder.Style = Resources["HeaderPanelStyle"] as Style;
                Grid.SetRow(headerBorder, 0);

                StackPanel headerPanel = new StackPanel();
                headerPanel.Orientation = Orientation.Horizontal;

                TextBlock headerText = new TextBlock();
                headerText.Text = "Multi-Strategy Manager";
                headerText.Style = Resources["HeaderTextStyle"] as Style;
                headerPanel.Children.Add(headerText);

                headerBorder.Child = headerPanel;
                mainGrid.Children.Add(headerBorder);

                // Create TabControl
                TabControl tabControl = new TabControl();
                tabControl.Margin = new Thickness(10);
                tabControl.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                tabControl.BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                Grid.SetRow(tabControl, 1);
                
                // Create General Settings Tab
                TabItem generalTab = new TabItem();
                generalTab.Header = "General Settings";
                generalTab.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                generalTab.Foreground = Brushes.White;
                
                // Create controls panel for general tab
                Border controlsBorder = new Border();
                controlsBorder.Style = Resources["ContentPanelStyle"] as Style;
                controlsBorder.Margin = new Thickness(10);

                Grid controlsGrid = new Grid();
                controlsGrid.Margin = new Thickness(5);

                // Define columns for the controls grid
                ColumnDefinition colDef1 = new ColumnDefinition();
                colDef1.Width = GridLength.Auto;
                controlsGrid.ColumnDefinitions.Add(colDef1);

                ColumnDefinition colDef2 = new ColumnDefinition();
                colDef2.Width = new GridLength(1, GridUnitType.Star);
                controlsGrid.ColumnDefinitions.Add(colDef2);

                ColumnDefinition colDef3 = new ColumnDefinition();
                colDef3.Width = GridLength.Auto;
                controlsGrid.ColumnDefinitions.Add(colDef3);

                ColumnDefinition colDef4 = new ColumnDefinition();
                colDef4.Width = new GridLength(1, GridUnitType.Star);
                controlsGrid.ColumnDefinitions.Add(colDef4);

                // Define rows for the controls grid
                RowDefinition ctrlRowDef1 = new RowDefinition();
                ctrlRowDef1.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef1);

                RowDefinition ctrlRowDef2 = new RowDefinition();
                ctrlRowDef2.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef2);

                RowDefinition ctrlRowDef3_new = new RowDefinition();
                ctrlRowDef3_new.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef3_new);

                // Account selection
                TextBlock accountLabel = new TextBlock();
                accountLabel.Text = "Account:";
                accountLabel.Style = Resources["FieldLabelStyle"] as Style;
                accountLabel.Margin = new Thickness(5);
                Grid.SetRow(accountLabel, 0);
                Grid.SetColumn(accountLabel, 0);
                controlsGrid.Children.Add(accountLabel);

                accountComboBox = new ComboBox();
                accountComboBox.Margin = new Thickness(5);
                accountComboBox.MinWidth = 150;
                accountComboBox.DisplayMemberPath = "DisplayName";
                accountComboBox.SelectionChanged += new SelectionChangedEventHandler(OnAccountSelectionChanged);

                // Create Refresh Button
                Button refreshButton = new Button();
                refreshButton.Content = "Refresh";
                refreshButton.Margin = new Thickness(5, 0, 0, 0); // Add some left margin
                refreshButton.Click += RefreshButton_Click;
                // Attempt to apply a style if available, otherwise default
                if (Resources.Contains("ModernButtonStyle"))
                    refreshButton.Style = Resources["ModernButtonStyle"] as Style;
                else if (Resources.Contains("StandardButtonStyle")) // Fallback to another common style
                    refreshButton.Style = Resources["StandardButtonStyle"] as Style;


                // Panel to hold Account ComboBox and Refresh Button
                StackPanel accountPanel = new StackPanel();
                accountPanel.Orientation = Orientation.Horizontal;
                accountPanel.Children.Add(accountComboBox);
                accountPanel.Children.Add(refreshButton);

                Grid.SetRow(accountPanel, 0);
                Grid.SetColumn(accountPanel, 1);
                controlsGrid.Children.Add(accountPanel);

                // Daily limits
                TextBlock limitsLabel = new TextBlock();
                limitsLabel.Text = "Daily Limits:";
                limitsLabel.Style = Resources["FieldLabelStyle"] as Style;
                limitsLabel.Margin = new Thickness(5);
                Grid.SetRow(limitsLabel, 0);
                Grid.SetColumn(limitsLabel, 2);
                controlsGrid.Children.Add(limitsLabel);

                StackPanel limitsPanel = new StackPanel();
                limitsPanel.Orientation = Orientation.Horizontal;
                limitsPanel.Margin = new Thickness(5);
                Grid.SetRow(limitsPanel, 0);
                Grid.SetColumn(limitsPanel, 3);

                TextBlock takeProfitLabel = new TextBlock();
                takeProfitLabel.Text = "Take Profit:";
                takeProfitLabel.Margin = new Thickness(0, 0, 5, 0);
                takeProfitLabel.VerticalAlignment = VerticalAlignment.Center;
                limitsPanel.Children.Add(takeProfitLabel);

                dailyTakeProfitInput = new TextBox();
                dailyTakeProfitInput.Width = 80;
                dailyTakeProfitInput.Text = dailyTakeProfit.ToString("F2");
                dailyTakeProfitInput.Margin = new Thickness(0, 0, 10, 0);
                dailyTakeProfitInput.LostFocus += DailyLimitInput_LostFocus; // Add event handler
                dailyTakeProfitInput.TextChanged += DailyLimitInput_TextChanged; // Add event handler
                limitsPanel.Children.Add(dailyTakeProfitInput);

                TextBlock lossLimitLabel = new TextBlock();
                lossLimitLabel.Text = "Loss Limit:";
                lossLimitLabel.Margin = new Thickness(0, 0, 5, 0);
                lossLimitLabel.VerticalAlignment = VerticalAlignment.Center;
                limitsPanel.Children.Add(lossLimitLabel);

                dailyLossLimitInput = new TextBox();
                dailyLossLimitInput.Width = 80;
                dailyLossLimitInput.Text = dailyLossLimit.ToString("F2");
                dailyLossLimitInput.LostFocus += DailyLimitInput_LostFocus; // Add event handler
                dailyLossLimitInput.TextChanged += DailyLimitInput_TextChanged; // Add event handler
                limitsPanel.Children.Add(dailyLossLimitInput);

                controlsGrid.Children.Add(limitsPanel);

                // Balance display
                TextBlock balanceLabel = new TextBlock();
                balanceLabel.Text = "Balance:";
                balanceLabel.Style = Resources["FieldLabelStyle"] as Style;
                balanceLabel.Margin = new Thickness(5);
                Grid.SetRow(balanceLabel, 1);
                Grid.SetColumn(balanceLabel, 0);
                controlsGrid.Children.Add(balanceLabel);

                StackPanel balancePanel = new StackPanel();
                balancePanel.Orientation = Orientation.Horizontal;
                balancePanel.Margin = new Thickness(5);
                Grid.SetRow(balancePanel, 1);
                Grid.SetColumn(balancePanel, 1);

                TextBlock realizedLabel = new TextBlock();
                realizedLabel.Text = "Realized:";
                realizedLabel.Margin = new Thickness(0, 0, 5, 0);
                balancePanel.Children.Add(realizedLabel);

                realizedBalanceText = new TextBlock { Margin = new Thickness(0, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };
                Binding realizedPnlTextBinding = new Binding("RealizedPnL") { StringFormat = "{0:C}", FallbackValue = "N/A" };
                realizedBalanceText.SetBinding(TextBlock.TextProperty, realizedPnlTextBinding);
                Binding realizedPnlColorBinding = new Binding("RealizedPnL") { Converter = (IValueConverter)Resources["PnlColorConverter"], FallbackValue = Brushes.Black };
                realizedBalanceText.SetBinding(TextBlock.ForegroundProperty, realizedPnlColorBinding);
                balancePanel.Children.Add(realizedBalanceText);

                TextBlock unrealizedLabel = new TextBlock { Text = "Unrealized:", Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                balancePanel.Children.Add(unrealizedLabel);

                unrealizedBalanceText = new TextBlock { Margin = new Thickness(0, 0, 15, 0), VerticalAlignment = VerticalAlignment.Center };
                Binding unrealizedPnlTextBinding = new Binding("UnrealizedPnL") { StringFormat = "{0:C}", FallbackValue = "N/A" };
                unrealizedBalanceText.SetBinding(TextBlock.TextProperty, unrealizedPnlTextBinding);
                Binding unrealizedPnlColorBinding = new Binding("UnrealizedPnL") { Converter = (IValueConverter)Resources["PnlColorConverter"], FallbackValue = Brushes.Black };
                unrealizedBalanceText.SetBinding(TextBlock.ForegroundProperty, unrealizedPnlColorBinding);
                balancePanel.Children.Add(unrealizedBalanceText);

                TextBlock totalPnlLabel = new TextBlock { Text = "Total:", Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                balancePanel.Children.Add(totalPnlLabel);

                totalPnlText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                Binding totalPnlTextBinding = new Binding("TotalPnL") { StringFormat = "{0:C}", FallbackValue = "N/A" };
                totalPnlText.SetBinding(TextBlock.TextProperty, totalPnlTextBinding);
                Binding totalPnlColorBinding = new Binding("TotalPnL") { Converter = (IValueConverter)Resources["PnlColorConverter"], FallbackValue = Brushes.Black };
                totalPnlText.SetBinding(TextBlock.ForegroundProperty, totalPnlColorBinding);
                balancePanel.Children.Add(totalPnlText);
 
                controlsGrid.Children.Add(balancePanel);

                // Enable/Disable toggle
                TextBlock enabledLabelText = new TextBlock(); // Renamed to avoid conflict if 'enabledLabel' is used elsewhere
                enabledLabelText.Text = "Tracking Status:";
                enabledLabelText.Style = Resources["FieldLabelStyle"] as Style;
                enabledLabelText.FontFamily = new FontFamily("Segoe UI");
                enabledLabelText.FontWeight = FontWeights.SemiBold;
                enabledLabelText.Margin = new Thickness(5, 0, 10, 0);
                enabledLabelText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(enabledLabelText, 1);
                Grid.SetColumn(enabledLabelText, 2);
                controlsGrid.Children.Add(enabledLabelText);

                // Panel for Status Toggle and Reset Button
                StackPanel statusControlsPanel = new StackPanel();
                statusControlsPanel.Orientation = Orientation.Horizontal;
                statusControlsPanel.Margin = new Thickness(5);

                enabledToggle = new ToggleButton();
                enabledToggle.Content = "Disabled";
                enabledToggle.Style = Resources["ModernToggleButtonStyle"] as Style;
                enabledToggle.FontFamily = new FontFamily("Segoe UI");
                enabledToggle.FontWeight = FontWeights.Medium;
                enabledToggle.VerticalAlignment = VerticalAlignment.Center;
                //enabledToggle.Margin = new Thickness(5); // Margin will be on the panel
                enabledToggle.Checked += new RoutedEventHandler(OnEnabledToggleChecked);
                enabledToggle.Unchecked += new RoutedEventHandler(OnEnabledToggleUnchecked);
                statusControlsPanel.Children.Add(enabledToggle);

                resetDailyStatusButton = new Button();
                resetDailyStatusButton.Content = "Reset Daily Status";
                resetDailyStatusButton.FontFamily = new FontFamily("Segoe UI");
                resetDailyStatusButton.FontWeight = FontWeights.Medium;
                if (Resources.Contains("ModernButtonStyle"))
                    resetDailyStatusButton.Style = Resources["ModernButtonStyle"] as Style;
                else if (Resources.Contains("StandardButtonStyle")) // Fallback
                    resetDailyStatusButton.Style = Resources["StandardButtonStyle"] as Style;
                resetDailyStatusButton.Margin = new Thickness(10, 0, 0, 0); // Add some left margin
                resetDailyStatusButton.VerticalAlignment = VerticalAlignment.Center;
                resetDailyStatusButton.Click += ResetDailyStatusButton_Click;
                statusControlsPanel.Children.Add(resetDailyStatusButton);

                flushExposureButton = new Button();
                flushExposureButton.Content = "Flush Exposure";
                flushExposureButton.FontFamily = new FontFamily("Segoe UI");
                flushExposureButton.FontWeight = FontWeights.Medium;
                if (Resources.Contains("ModernButtonStyle"))
                    flushExposureButton.Style = Resources["ModernButtonStyle"] as Style;
                else if (Resources.Contains("StandardButtonStyle"))
                    flushExposureButton.Style = Resources["StandardButtonStyle"] as Style;
                flushExposureButton.Margin = new Thickness(10, 0, 0, 0);
                flushExposureButton.VerticalAlignment = VerticalAlignment.Center;
                flushExposureButton.Click += FlushExposureButton_Click;
                statusControlsPanel.Children.Add(flushExposureButton);

                Grid.SetRow(statusControlsPanel, 1);
                Grid.SetColumn(statusControlsPanel, 3);
                controlsGrid.Children.Add(statusControlsPanel);

                // Create a GroupBox for Bridge Server settings
                GroupBox bridgeServerGroup = new GroupBox
                {
                    Header = "Bridge Server",
                    Margin = new Thickness(5),
                    Padding = new Thickness(5),
                    Style = Resources["ModernGroupBoxStyle"] as Style
                };
                Grid.SetRow(bridgeServerGroup, 2); // Use row 2 for Bridge Server
                Grid.SetColumn(bridgeServerGroup, 0);
                Grid.SetColumnSpan(bridgeServerGroup, 4); // Span across all columns

                // Create a grid for the Bridge Server content
                Grid bridgeServerGrid = new Grid();
                bridgeServerGrid.Margin = new Thickness(5);
                
                // Define columns for the Bridge Server grid
                bridgeServerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bridgeServerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bridgeServerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                // Define rows for the Bridge Server grid
                bridgeServerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                bridgeServerGroup.Content = bridgeServerGrid;

                // Bridge URL Label
                TextBlock bridgeUrlLabel = new TextBlock
                {
                    Text = "Bridge URL:",
                    Style = Resources["FieldLabelStyle"] as Style,
                    Margin = new Thickness(0, 5, 10, 5),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(bridgeUrlLabel, 0);
                Grid.SetColumn(bridgeUrlLabel, 0);
                bridgeServerGrid.Children.Add(bridgeUrlLabel);

                // Bridge URL TextBox
                grpcUrlInput = new TextBox
                {
                    Margin = new Thickness(0, 5, 10, 5),
                    Text = grpcServerAddress,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = Resources["ModernTextBoxStyle"] as Style
                };
                grpcUrlInput.TextChanged += GrpcUrlInput_TextChanged;
                Grid.SetRow(grpcUrlInput, 0);
                Grid.SetColumn(grpcUrlInput, 1);
                bridgeServerGrid.Children.Add(grpcUrlInput);

                // Ping Bridge Button (Enhanced with restart functionality)
                pingBridgeButton = new Button
                {
                    Content = "🔄 Restart Connection",
                    Style = Resources["ModernButtonStyle"] as Style,
                    Margin = new Thickness(0, 5, 0, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(10, 3, 10, 3),
                    ToolTip = "Restarts WebSocket connection, flushes logs, and tests bridge connectivity"
                };
                pingBridgeButton.Click += PingBridgeButton_Click;
                Grid.SetRow(pingBridgeButton, 0);
                Grid.SetColumn(pingBridgeButton, 2);
                bridgeServerGrid.Children.Add(pingBridgeButton);

                controlsGrid.Children.Add(bridgeServerGroup);

                // Add a new row for the SL/TP Management section
                RowDefinition ctrlRowDef4 = new RowDefinition();
                ctrlRowDef4.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef4);

                // SL/TP Management Section
                GroupBox sltpManagementGroup = new GroupBox
                {
                    Header = "SL/TP Management",
                    Margin = new Thickness(5),
                    Padding = new Thickness(5),
                    Style = Resources["ModernGroupBoxStyle"] as Style
                };
                Grid.SetRow(sltpManagementGroup, 3); // Use the new row for SL/TP settings
                Grid.SetColumn(sltpManagementGroup, 0);
                Grid.SetColumnSpan(sltpManagementGroup, 4); // Span across all columns

                // Create a Grid inside the GroupBox for better control over layout
                Grid sltpManagementGrid = new Grid();
                sltpManagementGrid.Margin = new Thickness(5);
                
                // Add a background color to the GroupBox header for better contrast
                sltpManagementGroup.HeaderTemplate = new DataTemplate();
                FrameworkElementFactory headerFactory = new FrameworkElementFactory(typeof(Border));
                headerFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
                headerFactory.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
                headerFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                
                FrameworkElementFactory headerTextFactory = new FrameworkElementFactory(typeof(TextBlock));
                headerTextFactory.SetValue(TextBlock.TextProperty, "SL/TP Management");
                headerTextFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
                headerTextFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                headerTextFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
                
                headerFactory.AppendChild(headerTextFactory);
                sltpManagementGroup.HeaderTemplate.VisualTree = headerFactory;
                
                // Define columns for the SL/TP management grid
                sltpManagementGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                sltpManagementGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                // Define rows for the SL/TP management grid
                sltpManagementGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                sltpManagementGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                
                sltpManagementGroup.Content = sltpManagementGrid;

                // Enable SL/TP Removal CheckBox - Row 0
                enableSLTPRemovalCheckBox = new CheckBox
                {
                    Content = "Enable SL/TP Order Removal",
                    Margin = new Thickness(0, 5, 0, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = Resources["ModernCheckBoxStyle"] as Style,
                    Foreground = new SolidColorBrush(Colors.White)  // Explicit white text for better visibility
                };
                Binding enableSLTPRemovalBinding = new Binding("EnableSLTPRemoval")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                };
                enableSLTPRemovalCheckBox.SetBinding(ToggleButton.IsCheckedProperty, enableSLTPRemovalBinding);
                Grid.SetRow(enableSLTPRemovalCheckBox, 0);
                Grid.SetColumn(enableSLTPRemovalCheckBox, 0);
                Grid.SetColumnSpan(enableSLTPRemovalCheckBox, 2);
                sltpManagementGrid.Children.Add(enableSLTPRemovalCheckBox);

                // SL/TP Removal Delay - Row 1
                TextBlock delayLabel = new TextBlock
                {
                    Text = "Removal Delay (seconds):",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 10, 5),
                    Style = Resources["ModernTextBlockStyle"] as Style,
                    Foreground = new SolidColorBrush(Colors.White)  // Explicit white text for better visibility
                };
                Grid.SetRow(delayLabel, 1);
                Grid.SetColumn(delayLabel, 0);
                sltpManagementGrid.Children.Add(delayLabel);
                
                sltpRemovalDelayTextBox = new TextBox
                {
                    Width = 80,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 5),
                    Style = Resources["ModernTextBoxStyle"] as Style,
                    Background = new SolidColorBrush(Color.FromRgb(70, 70, 70)),  // Slightly lighter background for visibility
                    Foreground = new SolidColorBrush(Colors.White)  // White text for better contrast
                };
                Binding sltpRemovalDelayBinding = new Binding("SLTPRemovalDelaySeconds")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ConverterCulture = CultureInfo.CurrentCulture // Ensure correct number parsing
                };
                sltpRemovalDelayTextBox.SetBinding(TextBox.TextProperty, sltpRemovalDelayBinding);
                Grid.SetRow(sltpRemovalDelayTextBox, 1);
                Grid.SetColumn(sltpRemovalDelayTextBox, 1);
                sltpManagementGrid.Children.Add(sltpRemovalDelayTextBox);

                controlsGrid.Children.Add(sltpManagementGroup);

                // Add a new row for the Elastic Hedging section
                RowDefinition ctrlRowDef5 = new RowDefinition();
                ctrlRowDef5.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef5);

                RowDefinition ctrlRowDef6 = new RowDefinition();
                ctrlRowDef6.Height = GridLength.Auto;
                controlsGrid.RowDefinitions.Add(ctrlRowDef6);

                // Create Elastic Hedging Management GroupBox
                GroupBox elasticHedgingGroup = new GroupBox
                {
                    Margin = new Thickness(5),
                    Padding = new Thickness(10),
                    Style = Resources["ModernGroupBoxStyle"] as Style
                };
                Grid.SetRow(elasticHedgingGroup, 4); // Use row 4 for Elastic Hedging
                Grid.SetColumn(elasticHedgingGroup, 0);
                Grid.SetColumnSpan(elasticHedgingGroup, 4); // Span across all columns

                // Add a custom header for better visibility (matching SL/TP section)
                elasticHedgingGroup.HeaderTemplate = new DataTemplate();
                FrameworkElementFactory elasticHeaderFactory = new FrameworkElementFactory(typeof(Border));
                elasticHeaderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
                elasticHeaderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));
                elasticHeaderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                
                FrameworkElementFactory elasticHeaderTextFactory = new FrameworkElementFactory(typeof(TextBlock));
                elasticHeaderTextFactory.SetValue(TextBlock.TextProperty, "Elastic Hedging Settings");
                elasticHeaderTextFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.White));
                elasticHeaderTextFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
                elasticHeaderTextFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
                
                elasticHeaderFactory.AppendChild(elasticHeaderTextFactory);
                elasticHedgingGroup.HeaderTemplate.VisualTree = elasticHeaderFactory;

                Grid elasticHedgingGrid = new Grid();
                elasticHedgingGrid.Margin = new Thickness(5);
                
                // Define columns with better spacing
                // 4 columns to match AddTypeValueSetting (type label, type combo, value label, value textbox)
                elasticHedgingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240, GridUnitType.Pixel) }); // Type label
                elasticHedgingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140, GridUnitType.Pixel) }); // Type input
                elasticHedgingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200, GridUnitType.Pixel) }); // Value label
                elasticHedgingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120, GridUnitType.Pixel) }); // Value input
                
                // Define rows with proper spacing (extra row to host Enable Trailing)
                for (int i = 0; i < 6; i++)
                {
                    elasticHedgingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                }

                // Enable Elastic Hedging CheckBox - Row 0
                CheckBox enableElasticHedgingCheckBox = new CheckBox
                {
                    Content = "Enable Elastic Hedging",
                    Margin = new Thickness(0, 5, 0, 15), // More bottom margin for spacing
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = Resources["ModernCheckBoxStyle"] as Style,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.SemiBold
                };
                Binding enableElasticHedgingBinding = new Binding("EnableElasticHedging")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                };
                enableElasticHedgingCheckBox.SetBinding(ToggleButton.IsCheckedProperty, enableElasticHedgingBinding);
                Grid.SetRow(enableElasticHedgingCheckBox, 0);
                Grid.SetColumn(enableElasticHedgingCheckBox, 0);
                Grid.SetColumnSpan(enableElasticHedgingCheckBox, 2);
                elasticHedgingGrid.Children.Add(enableElasticHedgingCheckBox);

                // Enable Trailing CheckBox (moved here from Trailing Settings tab) - Row 1
                CheckBox enableTrailingCheckBoxInline = new CheckBox
                {
                    Content = "Enable Trailing",
                    Margin = new Thickness(0, 0, 0, 10),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = Resources["ModernCheckBoxStyle"] as Style,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontWeight = FontWeights.SemiBold
                };
                Binding enableTrailingBindingInline = new Binding("EnableTrailing")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                };
                enableTrailingCheckBoxInline.SetBinding(ToggleButton.IsCheckedProperty, enableTrailingBindingInline);
                Grid.SetRow(enableTrailingCheckBoxInline, 1);
                Grid.SetColumn(enableTrailingCheckBoxInline, 0);
                Grid.SetColumnSpan(enableTrailingCheckBoxInline, 2);
                elasticHedgingGrid.Children.Add(enableTrailingCheckBoxInline);

                // UNIFIED SYSTEM: All settings now under Elastic Hedging Settings

                // Profit Update TRIGGER (Type + Value) - Row 2 - UNIFIED TRIGGER for both trailing and elastic
                AddTypeValueSetting(elasticHedgingGrid, 2, "Profit Update TRIGGER Type:", "Profit Update TRIGGER Value:",
                                    "TrailingTriggerType", "TrailingTriggerValue");

                // Trailing STOP (Type + Value) - Row 3 - Where to place initial stop when triggered
                AddTypeValueSetting(elasticHedgingGrid, 3, "Trailing STOP Type:", "Trailing STOP Value:",
                                    "TrailingStopType", "TrailingStopValue");

                // Increment Updates (Type + Value) - Row 4 - How both systems move together
                AddTypeValueSetting(elasticHedgingGrid, 4, "Increment Type:", "Increment Value:",
                                    "TrailingIncrementsType", "TrailingIncrementsValue");

                // Add info text to explain the unified system
                RowDefinition infoRow = new RowDefinition();
                infoRow.Height = GridLength.Auto;
                elasticHedgingGrid.RowDefinitions.Add(infoRow);

                TextBlock infoText = new TextBlock();
                infoText.Text = "ℹ️ UNIFIED SYSTEM: These settings control BOTH trailing stop loss AND elastic hedging together.\n" +
                               "• Profit Update TRIGGER: When to activate both systems\n" +
                               "• Trailing STOP: Where to place initial stop when triggered\n" +
                               "• Increment: How both systems move together in synchronized steps";
                infoText.TextWrapping = TextWrapping.Wrap;
                infoText.Foreground = new SolidColorBrush(Color.FromRgb(100, 150, 255));
                infoText.FontSize = 10;
                infoText.Margin = new Thickness(10, 10, 10, 5);
                Grid.SetRow(infoText, 5);
                Grid.SetColumn(infoText, 0);
                Grid.SetColumnSpan(infoText, 4);
                elasticHedgingGrid.Children.Add(infoText);

                elasticHedgingGroup.Content = elasticHedgingGrid;
                controlsGrid.Children.Add(elasticHedgingGroup);

                // ======================== Trailing Stop Settings Section ========================
                // REMOVED - Moved to dedicated Trailing Settings tab
                /*
                GroupBox trailingStopGroup = new GroupBox();
                trailingStopGroup.Style = Resources["SectionGroupBoxStyle"] as Style;
                Grid.SetRow(trailingStopGroup, 5);
                Grid.SetColumnSpan(trailingStopGroup, 4);
                trailingStopGroup.Margin = new Thickness(5, 5, 5, 5);
                trailingStopGroup.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                trailingStopGroup.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
                trailingStopGroup.BorderThickness = new Thickness(1);
                trailingStopGroup.Foreground = Brushes.White;

                // Create header with custom styling
                Border trailingStopHeaderBorder = new Border();
                trailingStopHeaderBorder.Background = new LinearGradientBrush(
                    Color.FromRgb(60, 60, 60), Color.FromRgb(40, 40, 40), 90);
                trailingStopHeaderBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                trailingStopHeaderBorder.BorderThickness = new Thickness(0, 0, 0, 1);
                trailingStopHeaderBorder.Padding = new Thickness(8, 4, 8, 4);

                TextBlock trailingStopHeaderText = new TextBlock();
                trailingStopHeaderText.Text = "🎯 Trailing Stop Settings";
                trailingStopHeaderText.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                trailingStopHeaderText.FontWeight = FontWeights.SemiBold;
                trailingStopHeaderText.FontSize = 11;
                trailingStopHeaderBorder.Child = trailingStopHeaderText;
                trailingStopGroup.Header = trailingStopHeaderBorder;

                Grid trailingStopGrid = new Grid();
                trailingStopGrid.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                
                // Define columns for trailing stop grid
                ColumnDefinition tsCol1 = new ColumnDefinition();
                tsCol1.Width = new GridLength(180, GridUnitType.Pixel);
                trailingStopGrid.ColumnDefinitions.Add(tsCol1);
                
                ColumnDefinition tsCol2 = new ColumnDefinition();
                tsCol2.Width = new GridLength(120, GridUnitType.Pixel);
                trailingStopGrid.ColumnDefinitions.Add(tsCol2);
                
                ColumnDefinition tsCol3 = new ColumnDefinition();
                tsCol3.Width = new GridLength(180, GridUnitType.Pixel);
                trailingStopGrid.ColumnDefinitions.Add(tsCol3);
                
                ColumnDefinition tsCol4 = new ColumnDefinition();
                tsCol4.Width = new GridLength(120, GridUnitType.Pixel);
                trailingStopGrid.ColumnDefinitions.Add(tsCol4);

                // Define rows for trailing stop grid
                RowDefinition tsRow1 = new RowDefinition();
                tsRow1.Height = GridLength.Auto;
                trailingStopGrid.RowDefinitions.Add(tsRow1);
                
                RowDefinition tsRow2 = new RowDefinition();
                tsRow2.Height = GridLength.Auto;
                trailingStopGrid.RowDefinitions.Add(tsRow2);
                
                RowDefinition tsRow3 = new RowDefinition();
                tsRow3.Height = GridLength.Auto;
                trailingStopGrid.RowDefinitions.Add(tsRow3);

                // Row 0: Enable Trailing Stop checkbox
                CheckBox enableTrailingStopCheckBox = new CheckBox();
                enableTrailingStopCheckBox.Content = "Enable Trailing Stop";
                enableTrailingStopCheckBox.Foreground = Brushes.White;
                enableTrailingStopCheckBox.Margin = new Thickness(10, 8, 5, 8);
                enableTrailingStopCheckBox.VerticalAlignment = VerticalAlignment.Center;
                Binding enableTrailingBinding = new Binding("EnableTrailingStop")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                };
                enableTrailingStopCheckBox.SetBinding(CheckBox.IsCheckedProperty, enableTrailingBinding);
                Grid.SetRow(enableTrailingStopCheckBox, 0);
                Grid.SetColumn(enableTrailingStopCheckBox, 0);
                Grid.SetColumnSpan(enableTrailingStopCheckBox, 2);
                trailingStopGrid.Children.Add(enableTrailingStopCheckBox);

                // Row 0: Activation threshold
                TextBlock activationLabel = new TextBlock();
                activationLabel.Text = "Activate After Pips Profit:";
                activationLabel.Foreground = Brushes.LightGray;
                activationLabel.Margin = new Thickness(10, 8, 5, 8);
                activationLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(activationLabel, 0);
                Grid.SetColumn(activationLabel, 2);
                trailingStopGrid.Children.Add(activationLabel);

                TextBox activationTextBox = new TextBox();
                activationTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
                activationTextBox.Margin = new Thickness(5, 5, 10, 5);
                Binding activationBinding = new Binding("ActivateTrailAfterPipsProfit")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ConverterCulture = CultureInfo.CurrentCulture
                };
                activationTextBox.SetBinding(TextBox.TextProperty, activationBinding);
                Grid.SetRow(activationTextBox, 0);
                Grid.SetColumn(activationTextBox, 3);
                trailingStopGrid.Children.Add(activationTextBox);

                // Row 1: Dollar trail distance
                TextBlock dollarTrailLabel = new TextBlock();
                dollarTrailLabel.Text = "Dollar Trail Distance ($):";
                dollarTrailLabel.Foreground = Brushes.LightGray;
                dollarTrailLabel.Margin = new Thickness(10, 8, 5, 8);
                dollarTrailLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(dollarTrailLabel, 1);
                Grid.SetColumn(dollarTrailLabel, 0);
                trailingStopGrid.Children.Add(dollarTrailLabel);

                TextBox dollarTrailTextBox = new TextBox();
                dollarTrailTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
                dollarTrailTextBox.Margin = new Thickness(5, 5, 10, 5);
                Binding dollarTrailBinding = new Binding("DollarTrailDistance")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ConverterCulture = CultureInfo.CurrentCulture
                };
                dollarTrailTextBox.SetBinding(TextBox.TextProperty, dollarTrailBinding);
                Grid.SetRow(dollarTrailTextBox, 1);
                Grid.SetColumn(dollarTrailTextBox, 1);
                trailingStopGrid.Children.Add(dollarTrailTextBox);

                // Row 1: Initial stop placement
                TextBlock initialStopLabel = new TextBlock();
                initialStopLabel.Text = "Initial Stop Placement:";
                initialStopLabel.Foreground = Brushes.LightGray;
                initialStopLabel.Margin = new Thickness(10, 8, 5, 8);
                initialStopLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(initialStopLabel, 1);
                Grid.SetColumn(initialStopLabel, 2);
                trailingStopGrid.Children.Add(initialStopLabel);

                ComboBox initialStopComboBox = new ComboBox();
                initialStopComboBox.Style = Resources["ModernComboBoxStyle"] as Style;
                initialStopComboBox.Margin = new Thickness(5, 5, 10, 5);
                initialStopComboBox.Items.Add("Start Trailing Immediately");
                initialStopComboBox.Items.Add("Move to Breakeven");
                initialStopComboBox.Items.Add("Fixed Pips From Entry");
                initialStopComboBox.Items.Add("Fixed Ticks From Entry");
                initialStopComboBox.Items.Add("Fixed Dollar From Entry");
                initialStopComboBox.SelectedIndex = 0;
                Grid.SetRow(initialStopComboBox, 1);
                Grid.SetColumn(initialStopComboBox, 3);
                trailingStopGrid.Children.Add(initialStopComboBox);

                // Row 2: ATR Period and Multiplier
                TextBlock atrPeriodLabel = new TextBlock();
                atrPeriodLabel.Text = "ATR Period:";
                atrPeriodLabel.Foreground = Brushes.LightGray;
                atrPeriodLabel.Margin = new Thickness(10, 8, 5, 8);
                atrPeriodLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(atrPeriodLabel, 2);
                Grid.SetColumn(atrPeriodLabel, 0);
                trailingStopGrid.Children.Add(atrPeriodLabel);

                TextBox atrPeriodTextBox = new TextBox();
                atrPeriodTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
                atrPeriodTextBox.Margin = new Thickness(5, 5, 10, 5);
                Binding atrPeriodBinding = new Binding("AtrPeriod")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ConverterCulture = CultureInfo.CurrentCulture
                };
                atrPeriodTextBox.SetBinding(TextBox.TextProperty, atrPeriodBinding);
                Grid.SetRow(atrPeriodTextBox, 2);
                Grid.SetColumn(atrPeriodTextBox, 1);
                trailingStopGrid.Children.Add(atrPeriodTextBox);

                TextBlock atrMultiplierLabel = new TextBlock();
                atrMultiplierLabel.Text = "ATR Multiplier:";
                atrMultiplierLabel.Foreground = Brushes.LightGray;
                atrMultiplierLabel.Margin = new Thickness(10, 8, 5, 8);
                atrMultiplierLabel.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(atrMultiplierLabel, 2);
                Grid.SetColumn(atrMultiplierLabel, 2);
                trailingStopGrid.Children.Add(atrMultiplierLabel);

                TextBox atrMultiplierTextBox = new TextBox();
                atrMultiplierTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
                atrMultiplierTextBox.Margin = new Thickness(5, 5, 10, 5);
                Binding atrMultiplierBinding = new Binding("AtrMultiplier")
                {
                    Source = MultiStratManager.Instance,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ConverterCulture = CultureInfo.CurrentCulture
                };
                atrMultiplierTextBox.SetBinding(TextBox.TextProperty, atrMultiplierBinding);
                Grid.SetRow(atrMultiplierTextBox, 2);
                Grid.SetColumn(atrMultiplierTextBox, 3);
                trailingStopGrid.Children.Add(atrMultiplierTextBox);

                trailingStopGroup.Content = trailingStopGrid;
                controlsGrid.Children.Add(trailingStopGroup);
                */
                
                controlsBorder.Child = controlsGrid;
                generalTab.Content = controlsBorder;
                tabControl.Items.Add(generalTab);
                
                // Create Trailing Settings Tab
                TabItem trailingTab = new TabItem();
                trailingTab.Header = "Trailing Settings";
                trailingTab.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                trailingTab.Foreground = Brushes.White;
                
                // Create the trailing settings content
                Grid trailingContent = CreateTrailingSettingsContent();
                trailingTab.Content = trailingContent;
                tabControl.Items.Add(trailingTab);
                
                // Create Strategy Monitor Tab
                TabItem strategyTab = new TabItem();
                strategyTab.Header = "Strategy Monitor";
                strategyTab.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                strategyTab.Foreground = Brushes.White;

                // Create DataGrid for strategies
                Border gridBorder = new Border();
                gridBorder.Style = Resources["ContentPanelStyle"] as Style;
                gridBorder.Margin = new Thickness(10);

                strategyGrid = new DataGrid();
                strategyGrid.AutoGenerateColumns = false;
                strategyGrid.IsReadOnly = false; // Overall grid not read-only to allow checkbox interaction
                strategyGrid.SelectionMode = DataGridSelectionMode.Single;
                strategyGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
                strategyGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
                strategyGrid.CanUserAddRows = false;
                strategyGrid.CanUserDeleteRows = false;
                strategyGrid.CanUserReorderColumns = true;
                strategyGrid.CanUserResizeColumns = true;
                strategyGrid.CanUserSortColumns = true;
                strategyGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
                strategyGrid.RowHeaderWidth = 0;
                strategyGrid.Margin = new Thickness(5);

                // Restore dark background and compact width/layout
                strategyGrid.Background = new SolidColorBrush(Color.FromRgb(51, 51, 51)); // #333333 dark, can adjust for preference
                strategyGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                strategyGrid.MaxWidth = 1020; // Prevents grid from stretching window; adjust as needed

                // Apply Modern Dark Theme Styles to the DataGrid
                if (Resources.Contains("ModernDataGridStyle"))
                    strategyGrid.Style = Resources["ModernDataGridStyle"] as Style;
                if (Resources.Contains("ModernDataGridRowStyle"))
                    strategyGrid.RowStyle = Resources["ModernDataGridRowStyle"] as Style;
                if (Resources.Contains("ModernDataGridCellStyle"))
                    strategyGrid.CellStyle = Resources["ModernDataGridCellStyle"] as Style;
                if (Resources.Contains("ModernDataGridColumnHeaderStyle"))
                    strategyGrid.ColumnHeaderStyle = Resources["ModernDataGridColumnHeaderStyle"] as Style;

                // Ensure the dark background color is applied after the style is set so it is not overridden.
                strategyGrid.Background = new SolidColorBrush(Color.FromRgb(45,45,48)); // #2D2D30, VS dark theme

                strategyGrid.CellEditEnding += StrategyGrid_CellEditEnding;

                // Style for DataGridRow: No hover highlight, click to select/highlight
                /* Style dataGridRowStyle = new Style(typeof(DataGridRow));
                dataGridRowStyle.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.Transparent));
                dataGridRowStyle.Setters.Add(new Setter(DataGridRow.BorderBrushProperty, Brushes.LightGray)); // Subtle border
                dataGridRowStyle.Setters.Add(new Setter(DataGridRow.BorderThicknessProperty, new Thickness(0,0,0,1)));

                Trigger rowMouseOverTrigger = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true };
                rowMouseOverTrigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Brushes.Transparent)); // No change on hover
                dataGridRowStyle.Triggers.Add(rowMouseOverTrigger);

                Trigger rowSelectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
                SolidColorBrush selectedBackground = SystemColors.HighlightBrush; // Default selection blue
                SolidColorBrush selectedForeground = SystemColors.HighlightTextBrush; // Default selection text (white)
                
                // Attempt to use theme brushes if available, otherwise use system defaults
                if (Resources.Contains("AccentColorBrush"))
                    selectedBackground = Resources["AccentColorBrush"] as SolidColorBrush ?? selectedBackground;
                if (Resources.Contains("IdealForegroundColorBrush")) // Common brush for text on accent
                    selectedForeground = Resources["IdealForegroundColorBrush"] as SolidColorBrush ?? selectedForeground;

                rowSelectedTrigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, selectedBackground));
                rowSelectedTrigger.Setters.Add(new Setter(DataGridRow.ForegroundProperty, selectedForeground));
                dataGridRowStyle.Triggers.Add(rowSelectedTrigger);
                strategyGrid.RowStyle = dataGridRowStyle;
                */
                // Style for CheckBox in "Enabled" column for hover highlight
                Style enabledCheckBoxStyle = new Style(typeof(CheckBox));
                enabledCheckBoxStyle.Setters.Add(new Setter(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                enabledCheckBoxStyle.Setters.Add(new Setter(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center));
                enabledCheckBoxStyle.Setters.Add(new Setter(CheckBox.MarginProperty, new Thickness(4))); // Add some padding around checkbox

                Trigger checkBoxMouseOverTrigger = new Trigger { Property = CheckBox.IsMouseOverProperty, Value = true };
                SolidColorBrush hoverBrush = (SolidColorBrush)(new BrushConverter().ConvertFrom("#FFE0E0E0")); // Light gray
                if (Resources.Contains("ControlHoverBrush"))
                     hoverBrush = Resources["ControlHoverBrush"] as SolidColorBrush ?? hoverBrush;
                checkBoxMouseOverTrigger.Setters.Add(new Setter(CheckBox.BackgroundProperty, hoverBrush));
                enabledCheckBoxStyle.Triggers.Add(checkBoxMouseOverTrigger);
                
                // Programmatically define all columns for strategyGrid
                strategyGrid.Columns.Add(new DataGridTextColumn {
                    Header = "Strategy Name",
                    Binding = new Binding("StrategyName"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 130,
                    MaxWidth = 170,
                });
                strategyGrid.Columns.Add(new DataGridTextColumn {
                    Header = "Account",
                    Binding = new Binding("AccountDisplayName"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 110,
                    MaxWidth = 150,
                });
                strategyGrid.Columns.Add(new DataGridTextColumn {
                    Header = "Instrument",
                    Binding = new Binding("InstrumentName"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 90,
                    MaxWidth = 130,
                });
                // REMOVED COLUMN: "Strategy Position"
                // strategyGrid.Columns.Add(new DataGridTextColumn {
                //     Header = "Strategy Position",
                //     Binding = new Binding("StrategyPosition"),
                //     IsReadOnly = true,
                //     Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                //     MinWidth = 90,
                //     MaxWidth = 120,
                // });
                strategyGrid.Columns.Add(new DataGridTextColumn {
                    Header = "Account Position",
                    Binding = new Binding("AccountPosition"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 80,
                    MaxWidth = 110,
                });
                // REMOVED COLUMN: "Average Price"
                // strategyGrid.Columns.Add(new DataGridTextColumn {
                //     Header = "Average Price",
                //     Binding = new Binding("AveragePrice"),
                //     IsReadOnly = true,
                //     Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                //     MinWidth = 80,
                //     MaxWidth = 110,
                // });
                strategyGrid.Columns.Add(new DataGridTextColumn {
                    Header = "Connected",
                    Binding = new Binding("ConnectionStatus"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                    MinWidth = 80,
                    MaxWidth = 110,
                });

                // Enabled (IsEnabled) - Using DataGridTemplateColumn
                DataGridTemplateColumn enabledColumn = new DataGridTemplateColumn();
                enabledColumn.Header = "Enabled";
                enabledColumn.MinWidth = 70;
                enabledColumn.MaxWidth = 80;
                
                DataTemplate cellTemplate = new DataTemplate();
                FrameworkElementFactory checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
                checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding("IsEnabled") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                checkBoxFactory.SetValue(CheckBox.StyleProperty, enabledCheckBoxStyle);
                // Attach the Click event handler
                checkBoxFactory.AddHandler(CheckBox.ClickEvent, new RoutedEventHandler(EnabledCheckBox_Click));
                cellTemplate.VisualTree = checkBoxFactory;
                
                enabledColumn.CellTemplate = cellTemplate;
                enabledColumn.CellEditingTemplate = cellTemplate; // Use same template for editing

                strategyGrid.Columns.Add(enabledColumn);
                
                gridBorder.Child = strategyGrid;
                strategyTab.Content = gridBorder;
                tabControl.Items.Add(strategyTab);
                
                // Active Trailing Stops Tab removed per request
                
                // Add the tabControl to mainGrid
                mainGrid.Children.Add(tabControl);
                
                // Set window content and force layout update
                Content = mainGrid;
                UpdateLayout();
                
                LogToBridge("INFO", "UI", "UI elements created successfully with new DataGrid styles and Enabled column template.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in CreateUI: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }
        
        private Grid CreateTrailingSettingsContent()
        {
            Grid mainGrid = new Grid();
            mainGrid.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            
            // Create scrollviewer for content
            ScrollViewer scrollViewer = new ScrollViewer();
            scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scrollViewer.Margin = new Thickness(10);
            
            // Content stack panel
            StackPanel contentPanel = new StackPanel();
            contentPanel.Margin = new Thickness(20);
            
            // Master Trailing Enable/Disable Section removed; moved under Elastic Hedging
            
            // Create DEMA-ATR Trailing Settings Section
            GroupBox demaGroup = CreateTrailingGroupBox("Trailing Settings");
            Grid demaGrid = CreateTrailingSettingsGrid();
            
            int currentRow = 0;
            
            // DEMA-ATR Trailing checkbox
            CheckBox demaCheckBox = new CheckBox();
            demaCheckBox.Content = "DEMA-ATR Trailing";
            demaCheckBox.Foreground = Brushes.White;
            demaCheckBox.Margin = new Thickness(10, 8, 5, 8);
            demaCheckBox.VerticalAlignment = VerticalAlignment.Center;
            demaCheckBox.Click += OnDemaTrailingChecked;
            
            Binding demaBinding = new Binding("UseATRTrailing")
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            demaCheckBox.SetBinding(CheckBox.IsCheckedProperty, demaBinding);
            Grid.SetRow(demaCheckBox, currentRow++);
            Grid.SetColumn(demaCheckBox, 0);
            Grid.SetColumnSpan(demaCheckBox, 2);
            demaGrid.Children.Add(demaCheckBox);
            
            // DEMA-ATR Period
            AddNumericSetting(demaGrid, currentRow++, "DEMA-ATR Period:", "DEMA_ATR_Period");
            
            // DEMA-ATR Multiplier
            AddDoubleSetting(demaGrid, currentRow++, "DEMA-ATR Multiplier:", "DEMA_ATR_Multiplier");
            
            // Trailing Activation with dropdown
            AddTrailingActivationSetting(demaGrid, currentRow++);
            
            demaGroup.Content = demaGrid;
            contentPanel.Children.Add(demaGroup);

            // NT Trade Run-Up Settings
            GroupBox runUpGroup = CreateTrailingGroupBox("NT Trade Run-Up");
            Grid runUpGrid = CreateTrailingSettingsGrid();
            int runUpRow = 0;

            // Enable Run-Up checkbox
            CheckBox runUpEnable = new CheckBox();
            runUpEnable.Content = "Enable NT Trade Run-Up (independent of Enable Trailing)";
            runUpEnable.Foreground = Brushes.White;
            runUpEnable.Margin = new Thickness(10, 8, 5, 8);
            runUpEnable.VerticalAlignment = VerticalAlignment.Center;
            Binding runUpEnableBinding = new Binding("EnableNtRunUp")
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            runUpEnable.SetBinding(CheckBox.IsCheckedProperty, runUpEnableBinding);
            Grid.SetRow(runUpEnable, runUpRow++);
            Grid.SetColumn(runUpEnable, 0);
            Grid.SetColumnSpan(runUpEnable, 2);
            runUpGrid.Children.Add(runUpEnable);

            // Initial trail distance
            AddRunUpTypeValueSetting(runUpGrid, runUpRow++, "Initial Distance Type:", "Initial Distance:", "NtRunUpDistanceUnits", "NtRunUpDistanceValue");
            // Increment step
            AddRunUpTypeValueSetting(runUpGrid, runUpRow++, "Increment Type:", "Increment Value:", "NtRunUpIncrementUnits", "NtRunUpIncrementValue");

            runUpGroup.Content = runUpGrid;
            contentPanel.Children.Add(runUpGroup);
            
            /*
            // Create Alternative Trailing Settings Section (DISABLED - merged into Elastic Hedging)
            GroupBox altGroup = CreateTrailingGroupBox("Alternative Trailing Settings");
            Grid altGrid = CreateTrailingSettingsGrid();
            
            currentRow = 0;
            
            // Trailing System Selection using same pattern as other type dropdowns
            AddTrailingSystemSetting(altGrid, currentRow++);
            
            // Use Alternative Trailing checkbox
            CheckBox altCheckBox = new CheckBox();
            altCheckBox.Content = "Use Alternative Trailing";
            altCheckBox.Foreground = Brushes.White;
            altCheckBox.Margin = new Thickness(10, 8, 5, 8);
            altCheckBox.VerticalAlignment = VerticalAlignment.Center;
            altCheckBox.Click += OnAlternativeTrailingChecked;
            
            Binding altBinding = new Binding("UseAlternativeTrailing")
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };
            altCheckBox.SetBinding(CheckBox.IsCheckedProperty, altBinding);
            Grid.SetRow(altCheckBox, currentRow++);
            Grid.SetColumn(altCheckBox, 0);
            Grid.SetColumnSpan(altCheckBox, 2);
            altGrid.Children.Add(altCheckBox);
            
            // Trailing TRIGGER settings
            AddTypeValueSetting(altGrid, currentRow++, "Trailing TRIGGER Type:", "Trailing TRIGGER Value:", 
                               "TrailingTriggerType", "TrailingTriggerValue");
            
            // Trailing STOP settings  
            AddTypeValueSetting(altGrid, currentRow++, "Trailing STOP Type:", "Trailing STOP Value:",
                               "TrailingStopType", "TrailingStopValue");
            
            // Trailing INCREMENTS settings
            AddTypeValueSetting(altGrid, currentRow++, "Trailing INCREMENTS Type:", "Trailing INCREMENTS Value:",
                               "TrailingIncrementsType", "TrailingIncrementsValue");
            
            altGroup.Content = altGrid;
            contentPanel.Children.Add(altGroup);
            
            // Add info section
            Border infoBorder = new Border();
            infoBorder.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            infoBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
            infoBorder.BorderThickness = new Thickness(1);
            infoBorder.CornerRadius = new CornerRadius(5);
            infoBorder.Margin = new Thickness(0, 20, 0, 0);
            infoBorder.Padding = new Thickness(15);
            
            StackPanel infoPanel = new StackPanel();
            
            TextBlock infoTitle = new TextBlock();
            infoTitle.Text = "ℹ️ Information";
            infoTitle.FontWeight = FontWeights.Bold;
            infoTitle.Foreground = new SolidColorBrush(Color.FromRgb(100, 150, 255));
            infoTitle.Margin = new Thickness(0, 0, 0, 10);
            infoPanel.Children.Add(infoTitle);
            
            TextBlock infoText = new TextBlock();
            infoText.Text = "Choose between DEMA-ATR trailing or Alternative trailing methods:\n\n" +
                           "🎯 DEMA-ATR Trailing:\n" +
                           "• DEMA-ATR Period: Number of bars for ATR calculation\n" +
                           "• DEMA-ATR Multiplier: Distance multiplier for trailing stop\n" +
                           "• Trailing Activation: When to activate the trailing stop based on profit\n\n" +
                           "⚙️ Alternative Trailing:\n" +
                           "• Trailing TRIGGER: Sets the profit level to start trailing\n" +
                           "• Trailing STOP: Initial profit amount to lock in\n" +
                           "• Trailing INCREMENTS: Additional profit locked per step\n" +
                           "• Each setting supports Ticks, Pips, Dollars, or Percent\n\n" +
                           "⚠️ Only one trailing method can be active at a time";
            infoText.TextWrapping = TextWrapping.Wrap;
            infoText.Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180));
            infoText.LineHeight = 18;
            infoPanel.Children.Add(infoText);
            
            infoBorder.Child = infoPanel;
            */
            // contentPanel.Children.Add(infoBorder); // Info panel disabled with the commented block above
            
            scrollViewer.Content = contentPanel;
            mainGrid.Children.Add(scrollViewer);
            
            return mainGrid;
        }
        
        private GroupBox CreateTrailingGroupBox(string headerText)
        {
            GroupBox groupBox = new GroupBox();
            groupBox.Style = Resources["SectionGroupBoxStyle"] as Style;
            groupBox.Margin = new Thickness(5, 5, 5, 15);
            groupBox.Background = new SolidColorBrush(Color.FromRgb(45, 45, 45));
            groupBox.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
            groupBox.BorderThickness = new Thickness(1);
            groupBox.Foreground = Brushes.White;
            
            // Create custom header to match General Settings style
            Border headerBorder = new Border();
            headerBorder.Background = new LinearGradientBrush(
                Color.FromRgb(60, 60, 60),
                Color.FromRgb(50, 50, 50),
                new Point(0, 0),
                new Point(0, 1));
            headerBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            headerBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            headerBorder.Padding = new Thickness(8, 4, 8, 4);
            
            TextBlock headerTextBlock = new TextBlock();
            headerTextBlock.Text = headerText;
            headerTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            headerTextBlock.FontWeight = FontWeights.SemiBold;
            headerTextBlock.FontSize = 11;
            headerBorder.Child = headerTextBlock;
            
            groupBox.Header = headerBorder;
            
            return groupBox;
        }
        
        private Grid CreateTrailingSettingsGrid()
        {
            Grid grid = new Grid();
            grid.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            
            // Define columns - adjusted for better layout
            ColumnDefinition labelCol = new ColumnDefinition();
            labelCol.Width = new GridLength(200, GridUnitType.Pixel);
            grid.ColumnDefinitions.Add(labelCol);
            
            ColumnDefinition inputCol = new ColumnDefinition();
            inputCol.Width = new GridLength(120, GridUnitType.Pixel);
            grid.ColumnDefinitions.Add(inputCol);
            
            ColumnDefinition labelCol2 = new ColumnDefinition();
            labelCol2.Width = new GridLength(180, GridUnitType.Pixel); // Increased from 150 to 180
            grid.ColumnDefinitions.Add(labelCol2);
            
            ColumnDefinition inputCol2 = new ColumnDefinition();
            inputCol2.Width = new GridLength(120, GridUnitType.Pixel); // Increased from 100 to 120
            grid.ColumnDefinitions.Add(inputCol2);
            
            ColumnDefinition spacerCol = new ColumnDefinition();
            spacerCol.Width = new GridLength(1, GridUnitType.Star);
            grid.ColumnDefinitions.Add(spacerCol);
            
            // Define rows (we'll add them as needed)
            for (int i = 0; i < 8; i++)
            {
                RowDefinition row = new RowDefinition();
                row.Height = GridLength.Auto;
                grid.RowDefinitions.Add(row);
            }
            
            return grid;
        }
        
        private void OnDemaTrailingChecked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            if (checkBox != null && MultiStratManager.Instance != null)
            {
                if (checkBox.IsChecked == true)
                {
                    // When DEMA-ATR is checked, uncheck Alternative
                    MultiStratManager.Instance.UseAlternativeTrailing = false;
                }
                // Note: When unchecked, we don't force it to false as that would prevent re-checking
            }
        }
        
        /// <summary>
        /// Handle trailing system dropdown selection change
        /// </summary>
        private void OnTrailingSystemSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                ComboBox comboBox = sender as ComboBox;
                if (comboBox != null && MultiStratManager.Instance != null)
                {
                    int selectedIndex = comboBox.SelectedIndex;
                    bool useTraditional = selectedIndex == 1; // Index 1 is Traditional Trailing
                    
                    LogToBridge("INFO", "SYSTEM", $"[UIForManager] Trailing system changed to: {(useTraditional ? "Traditional (Broker-Side)" : "Internal (Ultra-Fast)")}");
                    
                    MultiStratManager.Instance.UseTraditionalTrailing = useTraditional;
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"[UIForManager] Error in OnTrailingSystemSelectionChanged: {ex.Message}");
            }
        }
        
        private void OnAlternativeTrailingChecked(object sender, RoutedEventArgs e)
        {
            CheckBox checkBox = sender as CheckBox;
            if (checkBox != null && MultiStratManager.Instance != null)
            {
                if (checkBox.IsChecked == true)
                {
                    // When Alternative is checked, uncheck DEMA-ATR
                    MultiStratManager.Instance.UseATRTrailing = false;
                }
                // Note: When unchecked, we don't force it to false as that would prevent re-checking
            }
        }
        
        private void AddNumericSetting(Grid grid, int row, string label, string propertyName)
        {
            TextBlock labelBlock = new TextBlock();
            labelBlock.Text = label;
            labelBlock.Foreground = Brushes.LightGray;
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            labelBlock.Margin = new Thickness(0, 5, 10, 5);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
            
            TextBox textBox = new TextBox();
            textBox.Margin = new Thickness(0, 5, 0, 5);
            textBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            textBox.Foreground = Brushes.White;
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            textBox.Padding = new Thickness(5);
            if (Resources.Contains("ModernTextBoxStyle"))
                textBox.Style = Resources["ModernTextBoxStyle"] as Style;
            
            Binding binding = new Binding(propertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ConverterCulture = CultureInfo.CurrentCulture
            };
            textBox.SetBinding(TextBox.TextProperty, binding);
            
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
        }
        
        private void AddDoubleSetting(Grid grid, int row, string label, string propertyName)
        {
            TextBlock labelBlock = new TextBlock();
            labelBlock.Text = label;
            labelBlock.Foreground = Brushes.LightGray;
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            labelBlock.Margin = new Thickness(0, 5, 10, 5);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
            
            TextBox textBox = new TextBox();
            textBox.Margin = new Thickness(0, 5, 0, 5);
            textBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            textBox.Foreground = Brushes.White;
            textBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            textBox.Padding = new Thickness(5);
            if (Resources.Contains("ModernTextBoxStyle"))
                textBox.Style = Resources["ModernTextBoxStyle"] as Style;
            
            Binding binding = new Binding(propertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ConverterCulture = CultureInfo.CurrentCulture
            };
            textBox.SetBinding(TextBox.TextProperty, binding);
            
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
        }
        
        private void AddTrailingActivationSetting(Grid grid, int row)
        {
            // Label
            TextBlock labelBlock = new TextBlock();
            labelBlock.Text = "Trailing Activation:";
            labelBlock.Foreground = Brushes.LightGray;
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            labelBlock.Margin = new Thickness(0, 5, 10, 5);
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);
            
            // Create a sub-grid for dropdown and value
            Grid activationGrid = new Grid();
            ColumnDefinition dropdownCol = new ColumnDefinition();
            dropdownCol.Width = new GridLength(80, GridUnitType.Pixel);
            activationGrid.ColumnDefinitions.Add(dropdownCol);
            
            ColumnDefinition valueCol = new ColumnDefinition();
            valueCol.Width = new GridLength(65, GridUnitType.Pixel);
            activationGrid.ColumnDefinitions.Add(valueCol);
            
            // Dropdown
            ComboBox modeComboBox = new ComboBox();
            modeComboBox.Margin = new Thickness(0, 5, 5, 5);
            modeComboBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            modeComboBox.Foreground = Brushes.White;
            modeComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            if (Resources.Contains("ModernComboBoxStyle"))
                modeComboBox.Style = Resources["ModernComboBoxStyle"] as Style;
            
            modeComboBox.Items.Add("Ticks");
            modeComboBox.Items.Add("Pips");
            modeComboBox.Items.Add("Dollars");
            modeComboBox.Items.Add("Percent");
            
            Binding modeBinding = new Binding("TrailingActivationMode")
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = new EnumToStringConverter()
            };
            modeComboBox.SetBinding(ComboBox.SelectedValueProperty, modeBinding);
            modeComboBox.SelectedValue = "Percent"; // Default to Percent
            
            Grid.SetColumn(modeComboBox, 0);
            activationGrid.Children.Add(modeComboBox);
            
            // Value textbox
            TextBox valueTextBox = new TextBox();
            valueTextBox.Margin = new Thickness(0, 5, 0, 5);
            valueTextBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            valueTextBox.Foreground = Brushes.White;
            valueTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            valueTextBox.Padding = new Thickness(5);
            if (Resources.Contains("ModernTextBoxStyle"))
                valueTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
            
            Binding valueBinding = new Binding("TrailingActivationValue")
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ConverterCulture = CultureInfo.CurrentCulture
            };
            valueTextBox.SetBinding(TextBox.TextProperty, valueBinding);
            
            Grid.SetColumn(valueTextBox, 1);
            activationGrid.Children.Add(valueTextBox);
            
            Grid.SetRow(activationGrid, row);
            Grid.SetColumn(activationGrid, 1);
            grid.Children.Add(activationGrid);
        }
        
        private void AddTypeValueSetting(Grid grid, int row, string typeLabel, string valueLabel, 
                                        string typePropertyName, string valuePropertyName)
        {
            // Type label
            TextBlock typeLabelBlock = new TextBlock();
            typeLabelBlock.Text = typeLabel;
            typeLabelBlock.Foreground = Brushes.LightGray;
            typeLabelBlock.VerticalAlignment = VerticalAlignment.Center;
            typeLabelBlock.Margin = new Thickness(10, 5, 5, 5);
            Grid.SetRow(typeLabelBlock, row);
            Grid.SetColumn(typeLabelBlock, 0);
            grid.Children.Add(typeLabelBlock);
            
            // Type dropdown
            ComboBox typeComboBox = new ComboBox();
            typeComboBox.Margin = new Thickness(0, 5, 10, 5);
            typeComboBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            typeComboBox.Foreground = Brushes.White;
            typeComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            if (Resources.Contains("ModernComboBoxStyle"))
                typeComboBox.Style = Resources["ModernComboBoxStyle"] as Style;
            
            typeComboBox.Items.Add("Ticks");
            typeComboBox.Items.Add("Pips");
            typeComboBox.Items.Add("Dollars");
            typeComboBox.Items.Add("Percent");
            
            Binding typeBinding = new Binding(typePropertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = new EnumToStringConverter()
            };
            typeComboBox.SetBinding(ComboBox.SelectedValueProperty, typeBinding);
            typeComboBox.SelectedValue = "Dollars"; // Default
            
            Grid.SetRow(typeComboBox, row);
            Grid.SetColumn(typeComboBox, 1);
            grid.Children.Add(typeComboBox);
            
            // Value label
            TextBlock valueLabelBlock = new TextBlock();
            valueLabelBlock.Text = valueLabel;
            valueLabelBlock.Foreground = Brushes.LightGray;
            valueLabelBlock.VerticalAlignment = VerticalAlignment.Center;
            valueLabelBlock.Margin = new Thickness(10, 5, 5, 5);
            Grid.SetRow(valueLabelBlock, row);
            Grid.SetColumn(valueLabelBlock, 2);
            grid.Children.Add(valueLabelBlock);
            
            // Value textbox
            TextBox valueTextBox = new TextBox();
            valueTextBox.Margin = new Thickness(0, 5, 10, 5);
            valueTextBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            valueTextBox.Foreground = Brushes.White;
            valueTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            valueTextBox.Padding = new Thickness(5);
            if (Resources.Contains("ModernTextBoxStyle"))
                valueTextBox.Style = Resources["ModernTextBoxStyle"] as Style;
            
            Binding valueBinding = new Binding(valuePropertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ConverterCulture = CultureInfo.CurrentCulture
            };
            valueTextBox.SetBinding(TextBox.TextProperty, valueBinding);
            
            Grid.SetRow(valueTextBox, row);
            Grid.SetColumn(valueTextBox, 3);
            grid.Children.Add(valueTextBox);
        }

        private void AddRunUpTypeValueSetting(Grid grid, int row, string typeLabel, string valueLabel,
                                              string typePropertyName, string valuePropertyName)
        {
            // Type label
            TextBlock typeLabelBlock = new TextBlock();
            typeLabelBlock.Text = typeLabel;
            typeLabelBlock.Foreground = Brushes.LightGray;
            typeLabelBlock.VerticalAlignment = VerticalAlignment.Center;
            typeLabelBlock.Margin = new Thickness(10, 5, 5, 5);
            Grid.SetRow(typeLabelBlock, row);
            Grid.SetColumn(typeLabelBlock, 0);
            grid.Children.Add(typeLabelBlock);

            // Type dropdown (Ticks/Dollars only)
            ComboBox typeComboBox = new ComboBox();
            typeComboBox.Margin = new Thickness(0, 5, 10, 5);
            typeComboBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            typeComboBox.Foreground = Brushes.White;
            typeComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            if (Resources.Contains("ModernComboBoxStyle"))
                typeComboBox.Style = Resources["ModernComboBoxStyle"] as Style;

            typeComboBox.Items.Add("Ticks");
            typeComboBox.Items.Add("Dollars");

            Binding typeBinding = new Binding(typePropertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                Converter = new EnumToStringConverter()
            };
            typeComboBox.SetBinding(ComboBox.SelectedValueProperty, typeBinding);
            typeComboBox.SelectedValue = "Ticks";

            Grid.SetRow(typeComboBox, row);
            Grid.SetColumn(typeComboBox, 1);
            grid.Children.Add(typeComboBox);

            // Value label
            TextBlock valueLabelBlock = new TextBlock();
            valueLabelBlock.Text = valueLabel;
            valueLabelBlock.Foreground = Brushes.LightGray;
            valueLabelBlock.VerticalAlignment = VerticalAlignment.Center;
            valueLabelBlock.Margin = new Thickness(10, 5, 5, 5);
            Grid.SetRow(valueLabelBlock, row);
            Grid.SetColumn(valueLabelBlock, 2);
            grid.Children.Add(valueLabelBlock);

            // Value textbox
            TextBox valueTextBox = new TextBox();
            valueTextBox.Margin = new Thickness(0, 5, 10, 5);
            valueTextBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            valueTextBox.Foreground = Brushes.White;
            valueTextBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            valueTextBox.Padding = new Thickness(5);
            if (Resources.Contains("ModernTextBoxStyle"))
                valueTextBox.Style = Resources["ModernTextBoxStyle"] as Style;

            Binding valueBinding = new Binding(valuePropertyName)
            {
                Source = MultiStratManager.Instance,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                ConverterCulture = CultureInfo.CurrentCulture
            };
            valueTextBox.SetBinding(TextBox.TextProperty, valueBinding);

            Grid.SetRow(valueTextBox, row);
            Grid.SetColumn(valueTextBox, 3);
            grid.Children.Add(valueTextBox);
        }
        
        private void AddTrailingSystemSetting(Grid grid, int row)
        {
            // System label
            TextBlock systemLabelBlock = new TextBlock();
            systemLabelBlock.Text = "Trailing System:";
            systemLabelBlock.Foreground = Brushes.LightGray;
            systemLabelBlock.VerticalAlignment = VerticalAlignment.Center;
            systemLabelBlock.Margin = new Thickness(10, 5, 5, 5);
            Grid.SetRow(systemLabelBlock, row);
            Grid.SetColumn(systemLabelBlock, 0);
            grid.Children.Add(systemLabelBlock);
            
            // System dropdown
            ComboBox systemComboBox = new ComboBox();
            systemComboBox.Margin = new Thickness(0, 5, 10, 5);
            systemComboBox.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            systemComboBox.Foreground = Brushes.White;
            systemComboBox.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            if (Resources.Contains("ModernComboBoxStyle"))
                systemComboBox.Style = Resources["ModernComboBoxStyle"] as Style;
            
            systemComboBox.Items.Add("Internal Trailing (Ultra-Fast)");
            systemComboBox.Items.Add("Traditional Trailing (Broker-Side)");
            
            // Set initial selection based on current UseTraditionalTrailing property
            bool currentlyUsingTraditional = MultiStratManager.Instance?.UseTraditionalTrailing ?? false;
            systemComboBox.SelectedIndex = currentlyUsingTraditional ? 1 : 0;
            systemComboBox.SelectionChanged += OnTrailingSystemSelectionChanged;
            
            Grid.SetRow(systemComboBox, row);
            Grid.SetColumn(systemComboBox, 1);
            Grid.SetColumnSpan(systemComboBox, 3); // Span across remaining columns
            grid.Children.Add(systemComboBox);
        }

        private void UpdateAccountList()
        {
            try
            {
                LogToBridge("DEBUG", "UI", "Updating account list");
                if (accountComboBox == null)
                {
                    LogToBridge("ERROR", "UI", "ERROR: accountComboBox is null in UpdateAccountList");
                    return;
                }

                // Ensure we are on the UI thread for UI updates
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(UpdateAccountList));
                    return;
                }

                // Store the currently selected account
                Account previouslySelectedAccount = accountComboBox.SelectedItem as Account;

                accountComboBox.Items.Clear();
                lock (Account.All) // Lock for thread safety when accessing Account.All
                {
                    foreach (Account account in Account.All)
                    {
                        if (account.Connection != null && account.Connection.Status == NinjaTrader.Cbi.ConnectionStatus.Connected)
                        {
                            accountComboBox.Items.Add(account);
                            LogToBridge("DEBUG", "UI", $"Added account to dropdown: {account.DisplayName}");
                        }
                    }
                }

                // Attempt to re-select the previously selected account
                if (previouslySelectedAccount != null)
                {
                    bool reSelected = false;
                    foreach (Account account in accountComboBox.Items)
                    {
                        if (account.Name == previouslySelectedAccount.Name) // Compare by Name or other unique identifier
                        {
                            accountComboBox.SelectedItem = account;
                            selectedAccount = account; // Update the selectedAccount field
                            LogToBridge("DEBUG", "UI", $"Re-selected account: {account.DisplayName}");
                            reSelected = true;
                            break;
                        }
                    }
                    if (!reSelected)
                    {
                        // If the previously selected account is no longer in the list,
                        // the default selection (first item or none) will remain.
                        LogToBridge("WARN", "UI", $"Previously selected account '{previouslySelectedAccount.DisplayName}' not found after refresh.");
                    }
                }
                else if (accountComboBox.Items.Count > 0)
                {
                    // If no account was previously selected, or the list was empty,
                    // select the first item if available.
                    accountComboBox.SelectedIndex = 0;
                    selectedAccount = accountComboBox.SelectedItem as Account;
                    LogToBridge("DEBUG", "UI", $"Selected account: {(selectedAccount != null ? selectedAccount.DisplayName : "None")}");
                }
                else
                {
                    selectedAccount = null;
                    LogToBridge("WARN", "UI", "No accounts available to select.");
                }
                UpdateBalanceDisplay(); // Update balance for the initially selected account
                UpdateStrategyGrid(selectedAccount); // Update strategy grid for the initially selected account
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in UpdateAccountList: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }

        private void StartBalanceTracking()
        {
            try
            {
                Account.AccountStatusUpdate += OnAccountStatusUpdate;
                
                // Subscribe to account item updates for the selected account
                if (selectedAccount != null)
                {
                    selectedAccount.AccountItemUpdate += OnAccountUpdateHandler;
                    LogToBridge("DEBUG", "SYSTEM", $"Subscribed to AccountItemUpdate for account: {selectedAccount.Name}");
                }
                
                LogToBridge("INFO", "SYSTEM", "Balance tracking started. Subscribed to account events.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "SYSTEM", string.Format("ERROR in StartBalanceTracking: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }

        private void UpdateBalanceDisplay()
        {
            try
            {
                if (selectedAccount != null && realizedBalanceText != null && unrealizedBalanceText != null)
                {
                    // Ensure we are on the UI thread for UI updates
                    if (!Dispatcher.CheckAccess())
                    {
                        Dispatcher.BeginInvoke(new Action(UpdateBalanceDisplay));
                        return;
                    }

                    double realized = selectedAccount.GetAccountItem(AccountItem.RealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0;
                    double unrealized = selectedAccount.GetAccountItem(AccountItem.UnrealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0;

                    realizedBalanceText.Text = realized.ToString("C", CultureInfo.CurrentCulture);
                    unrealizedBalanceText.Text = unrealized.ToString("C", CultureInfo.CurrentCulture);
                    
                    // Log P&L values for verification (uncomment when debugging P&L issues)
                    // LogToBridge("DEBUG", "PNL", $"[UIForManager] Balance display updated for {selectedAccount.Name}: Realized={realized.ToString("C")}, Unrealized={unrealized.ToString("C")}");
                }
                else if (realizedBalanceText != null && unrealizedBalanceText != null)
                {
                    realizedBalanceText.Text = "$0.00";
                    unrealizedBalanceText.Text = "$0.00";
                    // Balance display cleared (no account selected).
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in UpdateBalanceDisplay: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }
        
        private void OnAccountStatusUpdate(object sender, EventArgs e)
        {
            // Update balance display on account status changes
            UpdateBalanceDisplay();
        }

        // Event handler for account selection change
        private void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (accountComboBox.SelectedItem is Account newSelectedAccount)
                {
                    // Unsubscribe from previous account-specific events if we had a selected account
                    if (selectedAccount != null)
                    {
                        selectedAccount.AccountItemUpdate -= OnAccountUpdateHandler;
                        LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Unsubscribed from AccountItemUpdate for previous account: {selectedAccount.Name}");
                    }
                    
                    // Set the new selected account
                    selectedAccount = newSelectedAccount;
                    LogToBridge("INFO", "UI", $"Account selection changed to: {selectedAccount.Name}");
                    
                    // Subscribe to account-specific events for the new account
                    selectedAccount.AccountItemUpdate += OnAccountUpdateHandler;
                    LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Subscribed to AccountItemUpdate for new account: {selectedAccount.Name}");
                    
                    // Force initial P&L retrieval and display update
                    UpdateBalanceDisplay();
                    
                    // Ensure the MultiStratManager knows about the account change
                    if (MultiStratManager.Instance != null)
                    {
                        MultiStratManager.Instance.SetMonitoredAccount(selectedAccount);
                    }
                    
                    UpdateStrategyGrid(selectedAccount); // Update strategy grid based on new selection

                    // Reset daily limit flag when account changes, as limits are per account
                    dailyLimitHitForSelectedAccountToday = false;
                    lastResetDate = DateTime.Today; // Also reset the date to ensure fresh check
                    if (enabledToggle != null && enabledToggle.Content.ToString() == "Limit Reached")
                    {
                        // If the global toggle was showing "Limit Reached", reset its text
                        // based on its actual IsChecked state.
                        enabledToggle.Content = enabledToggle.IsChecked == true ? "Enabled" : "Disabled";
                    }
                    LogToBridge("INFO", "SYSTEM", $"Daily P&L limit status reset due to account change to {selectedAccount.Name}.");
                }
                else
                {
                    selectedAccount = null;
                    LogToBridge("DEBUG", "UI", "Account selection cleared.");
                    
                    // Clear MultiStratManager reference to the account
                    if (MultiStratManager.Instance != null)
                    {
                        MultiStratManager.Instance.SetMonitoredAccount(null);
                    }
                    
                    UpdateBalanceDisplay();
                    UpdateStrategyGrid(null); // Clear strategy grid
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in OnAccountSelectionChanged: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }


        // Event handler for account updates
        private void OnAccountUpdateHandler(object sender, AccountItemEventArgs e)
        {
            try
            {
                // Ensure we are on the UI thread for UI updates
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(() => OnAccountUpdateHandler(sender, e)));
                    return;
                }
                
                // Process the update only if it's for the selected account
                if (e.Account == selectedAccount)
                {
                    // Check if this is a P&L related update (Unrealized or Realized P&L)
                    if (e.AccountItem == AccountItem.UnrealizedProfitLoss || e.AccountItem == AccountItem.RealizedProfitLoss)
                    {
                        // LogToBridge("DEBUG", "PNL", $"[UIForManager] Account item update received for {e.AccountItem}: {e.Value}");
                        UpdateBalanceDisplay();
                    }
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "SYSTEM", $"ERROR: [UIForManager] Unhandled exception in OnAccountUpdateHandler: {ex.Message} | StackTrace: {ex.StackTrace}");
            }
        }

        // Event handler for when the Enabled toggle is checked
        private void OnEnabledToggleChecked(object sender, RoutedEventArgs e)
        {
            try
            {
                enabledToggle.Content = "Enabled";
                LogToBridge("INFO", "SYSTEM", "Strategy tracking enabled.");
                // Start or resume strategy monitoring logic here if needed
                // For now, the P&L check in StrategyStatePollTimer_Tick will become active.
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in OnEnabledToggleChecked: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }
        
        private void DailyLimitInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            // This event is primarily for real-time feedback or complex validation if needed.
            // The main update logic is in LostFocus to avoid issues with partial input.
            // For now, we can just log or leave it empty if LostFocus handles the update.
            // Daily limit input text changed.
        }

        private void DailyLimitInput_LostFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToBridge("DEBUG", "UI", "Daily limit input lost focus.");
                if (double.TryParse(dailyTakeProfitInput.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out double newTakeProfit))
                {
                    dailyTakeProfit = newTakeProfit;
                    LogToBridge("INFO", "SYSTEM", $"Daily Take Profit updated to: {dailyTakeProfit.ToString("C")}");
                }
                else
                {
                    // Revert to old value or show error
                    dailyTakeProfitInput.Text = dailyTakeProfit.ToString("F2"); // Revert
                    LogToBridge("WARN", "UI", "Invalid input for Daily Take Profit. Reverted.");
                }

                if (double.TryParse(dailyLossLimitInput.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out double newLossLimit))
                {
                    dailyLossLimit = newLossLimit;
                    LogToBridge("INFO", "SYSTEM", $"Daily Loss Limit updated to: {dailyLossLimit.ToString("C")}");
                }
                else
                {
                    // Revert to old value or show error
                    dailyLossLimitInput.Text = dailyLossLimit.ToString("F2"); // Revert
                    LogToBridge("WARN", "UI", "Invalid input for Daily Loss Limit. Reverted.");
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in DailyLimitInput_LostFocus: {0}\n{1}", ex.Message, ex.StackTrace));
                // Optionally revert to old values on any error
                dailyTakeProfitInput.Text = dailyTakeProfit.ToString("F2");
                dailyLossLimitInput.Text = dailyLossLimit.ToString("F2");
            }
        }


        // Event handler for when the Enabled toggle is unchecked
        private void OnEnabledToggleUnchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                enabledToggle.Content = "Disabled";
                LogToBridge("INFO", "SYSTEM", "Strategy tracking disabled.");
                // Stop or pause strategy monitoring logic here if needed
                // The P&L check in StrategyStatePollTimer_Tick will become inactive.
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", string.Format("ERROR in OnEnabledToggleUnchecked: {0}\n{1}", ex.Message, ex.StackTrace));
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToBridge("DEBUG", "UI", "Refresh button clicked.");
                UpdateAccountList(); // This will re-populate accounts and trigger updates for balance and strategies
                // UpdateStrategyGrid(selectedAccount); // This is now called within UpdateAccountList and OnAccountSelectionChanged
                // UpdateBalanceDisplay(); // This is now called within UpdateAccountList and OnAccountSelectionChanged
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"Error during refresh: {ex.Message}");
            }
        }
private void ResetDailyStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToBridge("INFO", "UI", "Reset Daily Status button clicked.");
                dailyLimitHitForSelectedAccountToday = false;
                lastResetDate = DateTime.Today; // Ensure it's considered reset for today

                if (enabledToggle != null)
                {
                    // Reset the toggle button's content based on its actual checked state,
                    // not just assuming it should be "Enabled".
                    enabledToggle.Content = enabledToggle.IsChecked == true ? "Enabled" : "Disabled";
                }

                LogToBridge("INFO", "SYSTEM", $"Daily P&L limit status has been manually reset for account: {(selectedAccount != null ? selectedAccount.Name : "N/A")}. All strategies for this account may need to be manually re-enabled if they were disabled by the limit.");

                // Optionally, re-enable strategies that were disabled by the limit if desired,
                // but the message above suggests manual re-enablement.
                // If automatic re-enablement is needed:
                /*
                if (selectedAccount != null)
                {
                    var accountStrategies = MultiStratManager.GetStrategiesForAccount(selectedAccount.Name);
                    if (accountStrategies != null)
                    {
                        foreach (var strategyBase in accountStrategies)
                        {
                            if (strategyBase != null && strategyBase.State == State.Disabled)
                            {
                                // Check if this strategy was one that was auto-disabled by the limit
                                // This might require more sophisticated tracking or simply re-enable all disabled ones.
                                // For simplicity, let's assume we re-enable if the user wants this behavior.
                                // LogToBridge("DEBUG", "SYSTEM", $"Attempting to re-enable strategy {strategyBase.Name}");
                                // strategyBase.SetState(State.Active); // Or State.Realtime
                            }
                        }
                    }
                }
                UpdateStrategyGrid(selectedAccount); // Refresh grid to show updated states
                */
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"Error in ResetDailyStatusButton_Click: {ex.Message}");
            }
        }

        private void FlushExposureButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var confirmation = MessageBox.Show(
                    "This will clear the add-on's cross-strategy exposure tracking and rebuild it from currently open trades. Continue?",
                    "Flush Exposure State",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                var manager = MultiStratManager.Instance;
                if (manager == null)
                {
                    LogToBridge("WARN", "UI", "Exposure flush requested but MultiStratManager instance was null.");
                    return;
                }

                manager.FlushExposureState(true);
                LogToBridge("INFO", "UI", "Cross-strategy exposure ledger flushed via UI button.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"Error in FlushExposureButton_Click: {ex.Message}");
            }
        }

        private void GrpcUrlInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (grpcUrlInput != null)
            {
                grpcServerAddress = grpcUrlInput.Text;
                LogToBridge("DEBUG", "UI", $"[UIForManager] gRPC Server Address changed to: {grpcServerAddress}");

                // Update MultiStratManager with the new gRPC address
                if (MultiStratManager.Instance != null)
                {
                    LogToBridge("DEBUG", "UI", $"[UIForManager] gRPC address text changed. Updating MultiStratManager with address: {grpcServerAddress}");
                    MultiStratManager.Instance.SetGrpcAddress(grpcServerAddress);
                }
                // Basic validation (optional, as per instructions)
                if (string.IsNullOrWhiteSpace(grpcServerAddress))
                {
                    LogToBridge("WARN", "UI", "[UIForManager] Warning: gRPC Server Address is empty.");
                }
            }
        }

        private async void PingBridgeButton_Click(object sender, RoutedEventArgs e)
        {
            string url = grpcUrlInput.Text;
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(this, "gRPC Server Address is not set.", "Ping Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (MultiStratManager.Instance == null)
            {
                MessageBox.Show(this, "MultiStratManager instance is not available.", "Ping Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            SetIsWindowLocked(true); // Disable UI
            try
            {
                LogToBridge("INFO", "UI", "[UIForManager] Ping Bridge button clicked - Starting comprehensive connection restart");

                // Step 1: WebSocket removed - using gRPC only
                LogToBridge("INFO", "UI", "[UIForManager] Step 1: WebSocket restart removed - using gRPC only");

                // Step 2: Log flushing removed - using direct NinjaTrader output
                LogToBridge("INFO", "UI", "[UIForManager] Step 2: Log flushing removed - using direct NinjaTrader output");

                // Step 3: gRPC client reinitialization (event-based, no health check)
                LogToBridge("INFO", "UI", "[UIForManager] Step 3: Reinitializing gRPC client");
                
                // Force re-initialization - no health check per user requirements
                await MultiStratManager.Instance.ForceGrpcReinitialization();

                // Step 4: Report successful reinitialization (no connection test)
                string resultMessage = $"✅ gRPC Client Reinitialized Successfully!\n\n" +
                              $"🔄 Client: Restarted and ready for use\n" +
                              $"🌐 Server: {url}\n\n" +
                              $"Event-based connection management active.\n" +
                              $"Connection status will be determined when trades are sent.";

                LogToBridge("INFO", "UI", "[UIForManager] gRPC client reinitialized - ready for event-based usage");
                MessageBox.Show(this, resultMessage, "Client Reinitialized", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                string errorMessage = $"❌ Unexpected Error During Connection Restart\n\n" +
                                    $"Error: {ex.Message}\n\n" +
                                    $"Please try again or restart NinjaTrader if issues persist.";

                MessageBox.Show(this, errorMessage, "Restart Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogToBridge("ERROR", "UI", $"[UIForManager] PingBridgeButton_Click Exception: {ex.Message} | StackTrace: {ex.StackTrace}");
            }
            finally
            {
                SetIsWindowLocked(false); // Re-enable UI
            }
        }

        private void SetIsWindowLocked(bool isLocked)
        {
            // Disable/Enable key controls to prevent interaction during async operations
            if (accountComboBox != null) accountComboBox.IsEnabled = !isLocked;
            if (enabledToggle != null) enabledToggle.IsEnabled = !isLocked;
            if (resetDailyStatusButton != null) resetDailyStatusButton.IsEnabled = !isLocked;
            if (dailyTakeProfitInput != null) dailyTakeProfitInput.IsEnabled = !isLocked;
            if (dailyLossLimitInput != null) dailyLossLimitInput.IsEnabled = !isLocked;
            if (strategyGrid != null) strategyGrid.IsEnabled = !isLocked;
            if (grpcUrlInput != null) grpcUrlInput.IsEnabled = !isLocked;
            if (pingBridgeButton != null) pingBridgeButton.IsEnabled = !isLocked;
            
            // Attempt to find the Refresh button by its name if it was added with x:Name
            var refreshButton = this.FindName("RefreshButton") as Button; // Assuming RefreshButton has x:Name
            if (refreshButton == null)
            {
                // Fallback: If RefreshButton is not found by name, and assuming 'topPanel' exists and contains it.
                // This part is speculative as the exact structure of topPanel and RefreshButton isn't fully known from current context.
                // If 'topPanel' is a known container (e.g., a StackPanel field or found by FindName), we could iterate its children.
                // For example, if topPanel is a field:
                // if (topPanel != null) refreshButton = topPanel.Children.OfType<Button>().FirstOrDefault(b => b.Name == "RefreshButton" || b.Content?.ToString() == "Refresh");
                // For now, this specific fallback for RefreshButton might need adjustment if 'FindName' fails and 'topPanel' isn't directly accessible or named.
            }
            if (refreshButton != null) refreshButton.IsEnabled = !isLocked;

            Cursor = isLocked ? Cursors.Wait : Cursors.Arrow;
        }

        
        private void ApplyProgrammaticStyles()
        {
            try
            {
                // ModernWindowStyle
                Style modernWindowStyle = new Style(typeof(NTWindow));
                modernWindowStyle.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF2D2D30"))));
                modernWindowStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                modernWindowStyle.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
                modernWindowStyle.Setters.Add(new Setter(Window.WindowStyleProperty, WindowStyle.SingleBorderWindow));
                Resources["ModernWindowStyle"] = modernWindowStyle;

                // HeaderPanelStyle
                Style headerPanelStyle = new Style(typeof(Border));
                headerPanelStyle.Setters.Add(new Setter(Border.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF3F3F46"))));
                headerPanelStyle.Setters.Add(new Setter(Border.PaddingProperty, new Thickness(10)));
                Resources["HeaderPanelStyle"] = headerPanelStyle;

                // HeaderTextStyle
                Style headerTextStyle = new Style(typeof(TextBlock));
                headerTextStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 20.0));
                headerTextStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
                headerTextStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
                Resources["HeaderTextStyle"] = headerTextStyle;

                // ContentPanelStyle
                Style contentPanelStyle = new Style(typeof(Border));
                contentPanelStyle.Setters.Add(new Setter(Border.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF252526"))));
                contentPanelStyle.Setters.Add(new Setter(Border.BorderBrushProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF3F3F46"))));
                contentPanelStyle.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
                contentPanelStyle.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(3)));
                Resources["ContentPanelStyle"] = contentPanelStyle;

                // FieldLabelStyle
                Style fieldLabelStyle = new Style(typeof(TextBlock));
                fieldLabelStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.LightGray));
                fieldLabelStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
                fieldLabelStyle.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0,0,5,0)));
                Resources["FieldLabelStyle"] = fieldLabelStyle;

                // ModernToggleButtonStyle
                Style modernToggleButtonstyle = new Style(typeof(ToggleButton));
                modernToggleButtonstyle.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF007ACC")))); // Blue when checked
                modernToggleButtonstyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                modernToggleButtonstyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                modernToggleButtonstyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10,5,10,5)));
                modernToggleButtonstyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
                // Template for visual states (Checked/Unchecked)
                ControlTemplate toggleButtonTemplate = new ControlTemplate(typeof(ToggleButton));
                var border = new FrameworkElementFactory(typeof(Border), "border");
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                border.SetValue(Border.SnapsToDevicePixelsProperty, true);
                var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter), "contentPresenter");
                contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                contentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(5,2,5,2));
                border.AppendChild(contentPresenter);
                toggleButtonTemplate.VisualTree = border;
                // Triggers for visual states
                Trigger isCheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
                isCheckedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF007ACC")), "border")); // Blue
                isCheckedTrigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                Trigger isUncheckedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = false };
                isUncheckedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF555555")), "border")); // Dark Gray
                Trigger mouseOverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
                mouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF4A4A4A")), "border")); // Slightly lighter gray on hover
                
                toggleButtonTemplate.Triggers.Add(isCheckedTrigger);
                toggleButtonTemplate.Triggers.Add(isUncheckedTrigger);
                toggleButtonTemplate.Triggers.Add(mouseOverTrigger);
                modernToggleButtonstyle.Setters.Add(new Setter(Control.TemplateProperty, toggleButtonTemplate));
                Resources["ModernToggleButtonStyle"] = modernToggleButtonstyle;

                // ModernButtonStyle (for Reset button)
                Style modernButtonStyle = new Style(typeof(Button));
                modernButtonStyle.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF4F4F4F")))); // Darker Gray for regular buttons
                modernButtonStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                modernButtonStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                modernButtonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF6A6A6A"))));
                modernButtonStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10,5,10,5)));
                modernButtonStyle.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
                // Template for visual states
                ControlTemplate buttonTemplate = new ControlTemplate(typeof(Button));
                var btnBorder = new FrameworkElementFactory(typeof(Border), "border");
                btnBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
                btnBorder.SetValue(Border.SnapsToDevicePixelsProperty, true);
                btnBorder.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
                btnBorder.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
                btnBorder.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
                var btnContentPresenter = new FrameworkElementFactory(typeof(ContentPresenter), "contentPresenter");
                btnContentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                btnContentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                btnContentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(5,2,5,2));
                btnBorder.AppendChild(btnContentPresenter);
                buttonTemplate.VisualTree = btnBorder;
                // Triggers
                Trigger btnMouseOverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
                btnMouseOverTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF5A5A5A")), "border"));
                btnMouseOverTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF7A7A7A")), "border"));
                Trigger btnPressedTrigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
                btnPressedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF007ACC")), "border")); // Blue when pressed
                btnPressedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, (SolidColorBrush)(new BrushConverter().ConvertFrom("#FF005A9C")), "border"));
                
                buttonTemplate.Triggers.Add(btnMouseOverTrigger);
                buttonTemplate.Triggers.Add(btnPressedTrigger);
                modernButtonStyle.Setters.Add(new Setter(Control.TemplateProperty, buttonTemplate));
                Resources["ModernButtonStyle"] = modernButtonStyle;


                // DataGrid Styles (Copied from previous successful implementation)
                Resources["ModernDataGridStyle"] = Application.Current.TryFindResource("ModernDataGridStyle") ?? new Style(typeof(DataGrid));
                Resources["ModernDataGridRowStyle"] = Application.Current.TryFindResource("ModernDataGridRowStyle") ?? new Style(typeof(DataGridRow));
                Resources["ModernDataGridCellStyle"] = Application.Current.TryFindResource("ModernDataGridCellStyle") ?? new Style(typeof(DataGridCell));
                Resources["ModernDataGridColumnHeaderStyle"] = Application.Current.TryFindResource("ModernDataGridColumnHeaderStyle") ?? new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
                Resources["ControlHoverBrush"] = Application.Current.TryFindResource("ControlHoverBrush") ?? Brushes.LightGray;


                LogToBridge("DEBUG", "UI", "Programmatic styles applied.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"Error applying programmatic styles: {ex.Message}\n{ex.StackTrace}");
            }
        }


        private void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is CheckBox checkBox && checkBox.DataContext is StrategyDisplayInfo strategyInfo)
                {
                    bool isChecked = checkBox.IsChecked ?? false; // The new desired state from the checkbox click
                    LogToBridge("DEBUG", "UI", $"[UIForManager] EnabledCheckBox_Click: Strategy '{strategyInfo.StrategyName}', IsChecked (New Desired State): {isChecked}");

                    if (selectedAccount == null || string.IsNullOrEmpty(strategyInfo.StrategyName))
                    {
                        LogToBridge("WARN", "UI", "[UIForManager] EnabledCheckBox_Click: No selected account or strategy name is empty. Reverting checkbox.");
                        checkBox.IsChecked = !isChecked; // Revert
                        return;
                    }

                    // Ensure this strategy name is in the managed list for the current account if user is checking the box
                    if (isChecked)
                    {
                        if (!explicitlyManagedStrategySystemNamesByAccount.ContainsKey(selectedAccount.Name))
                        {
                            explicitlyManagedStrategySystemNamesByAccount[selectedAccount.Name] = new HashSet<string>();
                        }
                        if (explicitlyManagedStrategySystemNamesByAccount[selectedAccount.Name].Add(strategyInfo.StrategyName))
                        {
                             LogToBridge("INFO", "SYSTEM", $"[UIForManager] Added '{strategyInfo.StrategyName}' to explicitly managed list for account '{selectedAccount.Name}'.");
                        }
                    }
                    
                    // Handle attempt to enable/disable
                    if (strategyInfo.StrategyReference == null)
                    {
                        // This means the strategy is in our managed list but not currently live/found by UpdateStrategyGrid
                        if (isChecked) // User is trying to ENABLE a non-live strategy
                        {
                            LogToBridge("WARN", "SYSTEM", $"[UIForManager] User tried to ENABLE strategy '{strategyInfo.StrategyName}', but it has no live StrategyReference (Status: {strategyInfo.ConnectionStatus}). Action denied.");
                            MessageBox.Show($"Strategy '{strategyInfo.StrategyName}' is not currently active or loaded in NinjaTrader.\nPlease ensure the strategy is running in NinjaTrader to manage it here.", "Cannot Enable Strategy", MessageBoxButton.OK, MessageBoxImage.Warning);
                            checkBox.IsChecked = false; // Revert checkbox because we can't enable it
                            strategyInfo.IsEnabled = false; // Ensure model reflects this (it should be already, but defensive)
                        }
                        else // User is trying to DISABLE a non-live strategy
                        {
                             // If it's not live, it's already effectively "disabled". No API call needed.
                             // The UI state (IsEnabled=false) is already correct.
                            LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] User tried to DISABLE strategy '{strategyInfo.StrategyName}', which has no live StrategyReference. No API action needed.");
                            strategyInfo.IsEnabled = false; // Ensure model is false
                        }
                        return; // No further action if no live reference
                    }

                    // If we have a StrategyReference, proceed with state change via NinjaTrader API
                    // Check for daily limit before enabling
                    if (isChecked && dailyLimitHitForSelectedAccountToday && strategyInfo.AccountDisplayName == selectedAccount?.DisplayName)
                    {
                        LogToBridge("WARN", "SYSTEM", $"[UIForManager] Cannot enable strategy '{strategyInfo.StrategyName}'. Daily P&L limit has been hit for account '{selectedAccount.Name}'.");
                        MessageBox.Show($"Cannot enable strategy '{strategyInfo.StrategyName}'.\nDaily P&L limit has been hit for account '{selectedAccount.Name}'.\nReset 'Daily Status' to re-enable strategies for this account.", "Daily Limit Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                        checkBox.IsChecked = false; // Revert the checkbox
                        strategyInfo.IsEnabled = false; // Ensure model reflects this
                        return;
                    }

                    State targetState = isChecked ? State.Active : State.Terminated; // Or State.Realtime if preferred for enabling
                    LogToBridge("INFO", "SYSTEM", $"Attempting to set state of '{strategyInfo.StrategyName}' to {targetState}");

                    strategyInfo.StrategyReference.SetState(targetState);
                    
                    // Verify state change (optional, for debugging)
                    // Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                    //     State actualState = strategyInfo.StrategyReference.State;
                    //     LogToBridge("DEBUG", "SYSTEM", $"State of '{strategyInfo.StrategyName}' after SetState: {actualState}. Expected: {targetState}");
                    //     if(actualState != targetState && actualState != (isChecked ? State.Realtime : State.Disabled)) // Allow Realtime as valid enabled state
                    //     {
                    //        LogToBridge("WARN", "SYSTEM", $"WARNING: State mismatch for {strategyInfo.StrategyName}. UI might not reflect actual state.");
                    //        // Consider reverting UI if state change failed critically
                    //        // strategyInfo.IsEnabled = (actualState == State.Active || actualState == State.Realtime);
                    //        // checkBox.IsChecked = strategyInfo.IsEnabled;
                    //     }
                    // }));
                    
                    // Update the IsEnabled property which should trigger UI update via binding
                    strategyInfo.IsEnabled = isChecked; 
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"ERROR in EnabledCheckBox_Click: {ex.Message}\n{ex.StackTrace}");
                // Optionally revert checkbox on error
                if (sender is CheckBox cb) cb.IsChecked = !(cb.IsChecked ?? false);
            }
        }



        private void StrategyGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            // This event is primarily for handling edits in other editable columns if any were added.
            // For the "Enabled" CheckBox column, the click event is more direct.
            // However, if direct binding updates from CheckBox need to be committed, this can be a place.

            if (e.EditAction == DataGridEditAction.Commit)
            {
                if (e.Column is DataGridTemplateColumn templateColumn && templateColumn.Header.ToString() == "Enabled")
                {
                    if (e.Row.Item is StrategyDisplayInfo strategyInfo)
                    {
                        // The IsEnabled property in StrategyDisplayInfo should already be updated by two-way binding.
                        // This is more of a confirmation or a place for additional logic after commit.
                        LogToBridge("DEBUG", "UI", $"CellEditEnding for 'Enabled' column, Strategy: {strategyInfo.StrategyName}, IsEnabled: {strategyInfo.IsEnabled}");
                        
                        // The actual state change is handled by EnabledCheckBox_Click
                    }
                }
            }
        }
        private void OnWindowClosed(object sender, EventArgs e)
        {
            try
            {
                LogToBridge("INFO", "UI", "UIForManager window closed. Cleaning up resources.");
                
                // ✅ AGGRESSIVE FIX: Stop and dispose the polling timer completely
                if (strategyStatePollTimer != null)
                {
                    try
                    {
                        strategyStatePollTimer.Stop();
                        strategyStatePollTimer.Tick -= StrategyStatePollTimer_Tick;

                        // ✅ FORCE DISPOSAL: Use reflection to ensure complete cleanup
                        try
                        {
                            var disposeMethod = strategyStatePollTimer.GetType().GetMethod("Dispose", new Type[0]);
                            disposeMethod?.Invoke(strategyStatePollTimer, null);
                        }
                        catch { }

                        strategyStatePollTimer = null;
                        LogToBridge("DEBUG", "UI", "Strategy state polling timer aggressively stopped and disposed.");
                    }
                    catch (Exception timerEx)
                    {
                        LogToBridge("ERROR", "UI", $"Error stopping timer: {timerEx.Message}");
                    }
                }

                // Unsubscribe from global account events
                Account.AccountStatusUpdate -= OnAccountStatusUpdate;
                
                // Unsubscribe from UI-specific account events only
                if (selectedAccount != null)
                {
                    selectedAccount.AccountItemUpdate -= OnAccountUpdateHandler;
                    LogToBridge("DEBUG", "SYSTEM", $"[UIForManager] Unsubscribed from UI AccountItemUpdate for account: {selectedAccount.Name}");
                }

                // CRITICAL FIX: DO NOT call SetMonitoredAccount(null) - this would stop trade monitoring!
                // The MultiStratManager must continue monitoring trades even when UI is closed
                LogToBridge("DEBUG", "SYSTEM", "UI cleanup complete - trade monitoring continues in background.");

                // Unregister all strategies from monitoring
                if (activeStrategies != null)
                {
                    foreach (var stratInfo in activeStrategies)
                    {
                        if (stratInfo.StrategyReference != null)
                        {
                            MultiStratManager.UnregisterStrategyForMonitoring(stratInfo.StrategyReference);
                            LogToBridge("DEBUG", "SYSTEM", $"Unregistered strategy '{stratInfo.StrategyName}' from monitoring.");
                        }
                    }
                    activeStrategies.Clear();
                }

                // Unsubscribe from the PingReceivedFromBridge event
                if (MultiStratManager.Instance != null)
                {
                    MultiStratManager.Instance.PingReceivedFromBridge -= MultiStratManager_PingReceivedFromBridge;
                    LogToBridge("DEBUG", "UI", "[UIForManager] Unsubscribed from PingReceivedFromBridge event.");
                }

                // ✅ FIX: Disconnect gRPC asynchronously to prevent UI freezing
                Task.Run(async () =>
                {
                    try
                    {
                        LogToBridge("INFO", "UI", "[UIForManager] Starting async gRPC cleanup...");

                        // Use a timeout to prevent hanging
                        var timeoutTask = Task.Delay(5000); // 5 second timeout
                        var cleanupTask = Task.Run(() => MultiStratManager.Instance?.DisconnectGrpcAndStopAll());

                        var completedTask = await Task.WhenAny(cleanupTask, timeoutTask);

                        if (completedTask == timeoutTask)
                        {
                            LogToBridge("WARN", "UI", "[UIForManager] gRPC cleanup timed out after 5 seconds - continuing anyway");
                        }
                        else
                        {
                            LogToBridge("INFO", "UI", "[UIForManager] gRPC client disconnected successfully");
                        }
                    }
                    catch (Exception grpcEx)
                    {
                        LogToBridge("ERROR", "UI", $"[UIForManager] Error in async gRPC cleanup: {grpcEx.Message}");
                    }
                });
                
                // Nullify UI elements to help with garbage collection, though not strictly necessary with WPF's GC.
                accountComboBox = null;
                realizedBalanceText = null;
                unrealizedBalanceText = null;
                enabledToggle = null;
                resetDailyStatusButton = null;
                dailyTakeProfitInput = null;
                dailyLossLimitInput = null;
                strategyGrid = null;
                grpcUrlInput = null; // Clean up gRPC field

                // Any other cleanup
                LogToBridge("INFO", "UI", "UIForManager cleanup complete.");
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"Error during OnWindowClosed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void MultiStratManager_PingReceivedFromBridge()
        {
            // Ensure this runs on the UI thread
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(this, "Ping successfully received from bridge.", "Bridge Ping", MessageBoxButton.OK, MessageBoxImage.Information);
                LogToBridge("DEBUG", "UI", "[UIForManager] Displayed PingReceivedFromBridge MessageBox.");
            }));
        }
        
        /// <summary>
        /// Create trailing stops tab content
        /// </summary>
        private Grid CreateTrailingStopsContent()
        {
            Grid mainGrid = new Grid();
            mainGrid.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            
            // Add row definitions
            RowDefinition headerRow = new RowDefinition();
            headerRow.Height = GridLength.Auto;
            mainGrid.RowDefinitions.Add(headerRow);
            
            RowDefinition contentRow = new RowDefinition();
            contentRow.Height = new GridLength(1, GridUnitType.Star);
            mainGrid.RowDefinitions.Add(contentRow);
            
            // Create header with refresh button
            StackPanel headerPanel = new StackPanel();
            headerPanel.Orientation = Orientation.Horizontal;
            headerPanel.Margin = new Thickness(10, 10, 10, 5);
            
            Button refreshButton = new Button();
            refreshButton.Content = "Refresh";
            refreshButton.Width = 80;
            refreshButton.Height = 25;
            refreshButton.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            refreshButton.Foreground = Brushes.White;
            refreshButton.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            refreshButton.Click += RefreshTrailingStops_Click;
            headerPanel.Children.Add(refreshButton);
            
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);
            
            // Create main border
            Border gridBorder = new Border();
            gridBorder.Style = Resources["ContentPanelStyle"] as Style;
            gridBorder.Margin = new Thickness(10, 0, 10, 10);
            Grid.SetRow(gridBorder, 1);
            
            // Create trailing stops grid
            trailingStopsGrid = new DataGrid();
            trailingStopsGrid.AutoGenerateColumns = false;
            trailingStopsGrid.IsReadOnly = true;
            trailingStopsGrid.SelectionMode = DataGridSelectionMode.Single;
            trailingStopsGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
            trailingStopsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            trailingStopsGrid.CanUserAddRows = false;
            trailingStopsGrid.CanUserDeleteRows = false;
            trailingStopsGrid.CanUserResizeColumns = true;
            trailingStopsGrid.CanUserSortColumns = true;
            trailingStopsGrid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
            trailingStopsGrid.HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            trailingStopsGrid.Background = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            trailingStopsGrid.Foreground = Brushes.White;
            trailingStopsGrid.RowBackground = new SolidColorBrush(Color.FromRgb(45, 45, 45));
            trailingStopsGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(55, 55, 55));
            
            // Define columns
            DataGridTextColumn symbolColumn = new DataGridTextColumn();
            symbolColumn.Header = "Symbol";
            symbolColumn.Binding = new Binding("Symbol");
            symbolColumn.Width = 80;
            trailingStopsGrid.Columns.Add(symbolColumn);
            
            DataGridTextColumn directionColumn = new DataGridTextColumn();
            directionColumn.Header = "Direction";
            directionColumn.Binding = new Binding("Direction");
            directionColumn.Width = 60;
            trailingStopsGrid.Columns.Add(directionColumn);
            
            DataGridTextColumn entryColumn = new DataGridTextColumn();
            entryColumn.Header = "Entry";
            entryColumn.Binding = new Binding("EntryPrice") { StringFormat = "F2" };
            entryColumn.Width = 70;
            trailingStopsGrid.Columns.Add(entryColumn);
            
            DataGridTextColumn currentColumn = new DataGridTextColumn();
            currentColumn.Header = "Current";
            currentColumn.Binding = new Binding("CurrentPrice") { StringFormat = "F2" };
            currentColumn.Width = 70;
            trailingStopsGrid.Columns.Add(currentColumn);
            
            DataGridTextColumn stopColumn = new DataGridTextColumn();
            stopColumn.Header = "Stop Level";
            stopColumn.Binding = new Binding("StopLevel") { StringFormat = "F2" };
            stopColumn.Width = 80;
            trailingStopsGrid.Columns.Add(stopColumn);
            
            DataGridTextColumn pnlColumn = new DataGridTextColumn();
            pnlColumn.Header = "P&L";
            pnlColumn.Binding = new Binding("UnrealizedPnL") { StringFormat = "C2" };
            pnlColumn.Width = 80;
            pnlColumn.CellStyle = new Style(typeof(DataGridCell));
            pnlColumn.CellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new Binding("PnLBrush")));
            trailingStopsGrid.Columns.Add(pnlColumn);
            
            DataGridTextColumn distancePointsColumn = new DataGridTextColumn();
            distancePointsColumn.Header = "Distance (Pts)";
            distancePointsColumn.Binding = new Binding("StopDistancePoints") { StringFormat = "F2" };
            distancePointsColumn.Width = 90;
            trailingStopsGrid.Columns.Add(distancePointsColumn);
            
            DataGridTextColumn distancePercentColumn = new DataGridTextColumn();
            distancePercentColumn.Header = "Distance (%)";
            distancePercentColumn.Binding = new Binding("StopDistancePercent") { StringFormat = "F2" };
            distancePercentColumn.Width = 90;
            distancePercentColumn.CellStyle = new Style(typeof(DataGridCell));
            distancePercentColumn.CellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new Binding("StopDistanceBrush")));
            trailingStopsGrid.Columns.Add(distancePercentColumn);
            
            DataGridTextColumn maxProfitColumn = new DataGridTextColumn();
            maxProfitColumn.Header = "Max Profit";
            maxProfitColumn.Binding = new Binding("MaxProfit") { StringFormat = "C2" };
            maxProfitColumn.Width = 80;
            trailingStopsGrid.Columns.Add(maxProfitColumn);
            
            DataGridTextColumn updateCountColumn = new DataGridTextColumn();
            updateCountColumn.Header = "Updates";
            updateCountColumn.Binding = new Binding("UpdateCount");
            updateCountColumn.Width = 60;
            trailingStopsGrid.Columns.Add(updateCountColumn);
            
            DataGridTextColumn statusColumn = new DataGridTextColumn();
            statusColumn.Header = "Status";
            statusColumn.Binding = new Binding("Status");
            statusColumn.Width = 80;
            trailingStopsGrid.Columns.Add(statusColumn);
            
            // Set ItemsSource
            trailingStopsGrid.ItemsSource = activeTrailingStops;
            
            gridBorder.Child = trailingStopsGrid;
            mainGrid.Children.Add(gridBorder);
            
            return mainGrid;
        }
        
        // Prevent repetitive warning spam when data context isn't ready
        private static bool s_loggedNullTrailingOnce = false;

        /// <summary>
        /// Update trailing stops display with current data
        /// </summary>
        private void UpdateTrailingStopsDisplay()
        {
            try
            {
                if (MultiStratManager.Instance == null || activeTrailingStops == null)
                {
                    if (!s_loggedNullTrailingOnce)
                    {
                        s_loggedNullTrailingOnce = true;
                        LogToBridge("WARN", "UI", "[UIForManager] UpdateTrailingStopsDisplay: MultiStratManager.Instance or activeTrailingStops is null (will suppress repeats)");
                    }
                    return;
                }
                // Reset once the UI has valid data
                if (s_loggedNullTrailingOnce)
                    s_loggedNullTrailingOnce = false;
                
                // Internal trailing has been removed. Display broker-side traditional trailing stops instead.
                var traditionalStops = MultiStratManager.Instance.TraditionalTrailingStops;
                
                // Clear existing items
                activeTrailingStops.Clear();
                
                // Add current trailing stops
                foreach (var kvp in traditionalStops)
                {
                    var stop = kvp.Value;
                    // Processing stop
                    if (!stop.IsActive) continue;

                    double currentPrice = GetCurrentMarketPrice(stop.TrackedPosition?.Instrument);
                    // Current price retrieved
                    
                    if (currentPrice == 0) continue;
                    
                    double unrealizedPnL = 0;
                    double stopDistancePoints = 0;
                    double stopDistancePercent = 0;
                    
                    if (stop.TrackedPosition != null)
                    {
                        unrealizedPnL = stop.TrackedPosition.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);

                        if (stop.TrackedPosition.MarketPosition == MarketPosition.Long)
                        {
                            stopDistancePoints = currentPrice - stop.LastStopPrice;
                        }
                        else
                        {
                            stopDistancePoints = stop.LastStopPrice - currentPrice;
                        }
                        
                        stopDistancePercent = Math.Abs(stopDistancePoints / currentPrice) * 100;
                    }
                    
                    var displayInfo = new TrailingStopDisplayInfo
                    {
                        BaseId = stop.BaseId,
                        Symbol = stop.TrackedPosition?.Instrument?.MasterInstrument?.Name ?? "Unknown",
                        Direction = stop.TrackedPosition?.MarketPosition.ToString() ?? "Unknown",
                        EntryPrice = stop.EntryPrice,
                        CurrentPrice = currentPrice,
                        StopLevel = stop.LastStopPrice,
                        UnrealizedPnL = unrealizedPnL,
                        StopDistancePoints = stopDistancePoints,
                        StopDistancePercent = stopDistancePercent,
                        Status = stop.CurrentStopOrder?.OrderState.ToString() ?? "Active",
                        ActivationTime = stop.ActivationTime,
                        UpdateCount = stop.ModificationCount,
                        MaxProfit = stop.MaxProfit
                    };
                    
                    activeTrailingStops.Add(displayInfo);
                    LogToBridge("DEBUG", "TRAILING", $"[UIForManager] Added trailing stop display for {stop.BaseId}");
                }
                
                // Final count updated in UI
                
                // FOR DEBUGGING: Add test data if no real trailing stops exist
                if (activeTrailingStops.Count == 0)
                {
                    AddTestTrailingStopData();
                }
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "TRAILING", $"Error updating trailing stops display: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Add test data to verify UI is working (for debugging)
        /// </summary>
        private void AddTestTrailingStopData()
        {
            try
            {
                if (activeTrailingStops == null) return;
                
                // Add a test trailing stop entry
                var testStop = new TrailingStopDisplayInfo
                {
                    BaseId = "TEST_123",
                    Symbol = "NQ",
                    Direction = "Long",
                    EntryPrice = 21850.0,
                    CurrentPrice = 21875.0,
                    StopLevel = 21825.0,
                    UnrealizedPnL = 125.0,
                    StopDistancePoints = 50.0,
                    StopDistancePercent = 0.23,
                    Status = "Active",
                    ActivationTime = DateTime.UtcNow.AddMinutes(-5),
                    UpdateCount = 3,
                    MaxProfit = 150.0
                };
                
                activeTrailingStops.Add(testStop);
                // Added test trailing stop data
            }
            catch (Exception ex)
            {
            }
        }
        
        /// <summary>
        /// Get current market price for an instrument
        /// </summary>
        private double GetCurrentMarketPrice(Instrument instrument)
        {
            if (instrument?.MarketData == null) return 0;
            
            double bid = instrument.MarketData.Bid?.Price ?? 0;
            double ask = instrument.MarketData.Ask?.Price ?? 0;
            
            if (bid > 0 && ask > 0)
                return (bid + ask) / 2;
            else if (instrument.MarketData.Last != null)
                return instrument.MarketData.Last.Price;
            else
                return 0;
        }
        
        /// <summary>
        /// Manual refresh button click handler
        /// </summary>
        private void RefreshTrailingStops_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToBridge("DEBUG", "UI", "[UIForManager] Manual refresh of trailing stops requested");
                UpdateTrailingStopsDisplay();
            }
            catch (Exception ex)
            {
                LogToBridge("ERROR", "UI", $"[UIForManager] Error in manual refresh: {ex.Message}");
            }
        }
    }
}
