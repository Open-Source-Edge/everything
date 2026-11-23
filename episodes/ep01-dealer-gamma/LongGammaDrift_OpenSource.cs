// ============================================================================
// OpenSourceEdge - Build Order #01: Shield Battery (Long-Gamma Drift, NT8)
// ============================================================================
// Author:  @OpenSourceEdge
// License: MIT (Free to use, modify, fork, and backtest)
// Scouted on 1B+ real ticks & 32M options rows. Strictly causal. No map hacks.
// Disclaimer: Educational and quantitative research software only. Not
// financial advice. Verify your broker or prop firm rules before running
// automated order execution.
//
// MECHANISM:
// When ES opens at 09:30 ET above the causally mapped pre-market SPX 14-DTE
// Gamma Flip (Positive Gamma), options dealers hedge by buying dips and
// selling rips. As daily options decay in the afternoon, volatility stays
// suppressed and equities tend to grind steadily higher into the close.
//
// RULES:
// 1. Pre-market regime: ES 09:30 ET open > mapped SPX 14-DTE Gamma Flip:
//      flip_es = es_prior_close * (d14_gamma_flip / spx_prior_close)
//    (Fallback if flip is blank: d14_net_gex_b > 0).
// 2. Pause on scheduled Fed FOMC decision days (built-in 2019-2026 calendar).
// 3. Optional dual-period regime vetoes (all pass through if column missing):
//    - VIX9D/VIX >= 1.05 (short-term vol inversion)
//    - QQQ 60-day put/call volume percentile <= 0.15 (call frenzy)
// 4. Signal: at 13:00 ET, if MNQ closes strictly above RTH VWAP (09:30+),
//    buy at market on the 1-tick execution series.
// 5. Risk: hard stop at max(16 ticks, round(2.0 x ATR15)), capped by MaxStopPoints.
// 6. Exit: flat at 15:55 ET (hard safety clock at 15:56 ET). Max 1 trade/day.
//
// CHART SETUP:
// - Instrument: MNQ (front contract, e.g. MNQ 12-26)
// - Bars: 1 Minute | Trading Hours: CME US Index Futures ETH | Days to load: 40+
// - CSV Path: defaults to Documents\NinjaTrader 8\OpenSourceEdge\daily_regime.csv
//   (auto-reloads live in real time whenever updated pre-market)
// ============================================================================

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies.OpenSourceEdgeLgCore;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class LongGammaDriftOpenSource : Strategy
    {
        private LgCore core;
        private LgRegimeTable regime;
        private bool refused, runWritten, optimizing;
        private string configProblem, esName = "", resolvedCsvPath = "";
        private TimeZoneInfo eastern;
        private DateTime offsetKey = DateTime.MinValue;
        private TimeSpan offsetToEastern;
        private StreamWriter writer;
        private SessionIterator sessionIterator;
        private System.Timers.Timer clock;
        private int checkMinute, exitMinute;
        private DateTime lastTradeEt = DateTime.MinValue;

        private DateTime railDay = DateTime.MinValue, entryDay = DateTime.MinValue;
        private bool haltedToday, exitPending;
        private double realizedToday;
        private string exitPendingName = "";
        private LgDecision todaySignal;

        private int openQty, closedQty, openStopTicks;
        private DateTime openTime, lastExitTime;
        private double openValue, exitValue, openCommission, exitCommission;
        private string openNote = "", lastExitName = "";
        private int tradesLogged, signalsPlaced;

        private const int TickIndex = 1;
        private const int EsIndex = 2;
        private const string LongName = "OSE LG Long";
        private const string FlatName = "OSE LG Flat";
        private const string SafetyName = "OSE LG Safety";
        private const string DailyLossName = "OSE LG DLL";
        private static readonly TimeSpan HardFlat = new TimeSpan(15, 56, 0);

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "OpenSourceEdge Build Order #01: Shield Battery (Long-Gamma Afternoon Drift). When ES opens above the causally mapped SPX 14-DTE Gamma Flip (Positive Gamma) and MNQ closes above RTH VWAP at 13:00 ET, buy with a 2x ATR15 hard stop and exit flat at 15:55 ET.";
                Name = "LongGammaDriftOpenSource";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 1;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Day;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 0;
                IsInstantiatedOnEachOptimizationIteration = true;

                Quantity = 1;
                CheckTimeEt = 1300;
                ExitTimeEt = 1555;
                StopAtrMultiple = 2.0;
                MinStopTicks = 16;
                MaxStopPoints = 200;
                SkipFomcDays = true;
                SkipQqqCallFrenzy = false;
                QqqCallFrenzyMaxPct = 0.15;
                SkipVix9dInverted = false;
                MaxVix9dRatio = 1.05;
                RegimeCsvPath = "";
                EsInstrument = "";
                ExtraFomcDates = "";
                MaxDailyLossDollars = 1000;
                AllowOtherInstruments = false;
                AllowSharedAccountPositions = true;
                BlockOpposingAccountPosition = true;
                WriteCsv = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Tick, 1);
                string suffix = ContractSuffix();
                esName = IsAuto(EsInstrument) ? "ES " + suffix : EsInstrument.Trim();
                if (!InstrumentExists(esName)) esName = "ES ##-##";
                if (!InstrumentExists(esName))
                {
                    configProblem = "ES reference instrument " + esName + " was not found. Set 'ES instrument' explicitly (for example ES 12-26).";
                    return;
                }
                AddDataSeries(esName, Data.BarsPeriodType.Minute, 1);
            }
            else if (State == State.DataLoaded)
            {
                optimizing = Category == Category.Optimize || Category == Category.WalkForward || Category == Category.MultiObjective;
                try { eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { eastern = TimeZoneInfo.Local; Say("Eastern time zone not found, using local time."); }

                string why = configProblem ?? StartProblem();
                if (why != null)
                {
                    refused = true;
                    Say("REFUSED TO START: " + why + " No orders will be placed.");
                    try { Log("LongGammaDriftOpenSource refused to start: " + why, LogLevel.Error); } catch { }
                    return;
                }

                resolvedCsvPath = string.IsNullOrWhiteSpace(RegimeCsvPath)
                    ? Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "OpenSourceEdge", "daily_regime.csv")
                    : RegimeCsvPath.Trim();

                regime = new LgRegimeTable(resolvedCsvPath);
                regime.ReloadIfChanged();

                core = new LgCore();
                core.CheckMinute = checkMinute;
                core.ExitMinute = exitMinute;
                core.StopAtrMult = StopAtrMultiple;
                core.MinStopTicks = MinStopTicks;
                core.MaxStopTicks = MaxStopPoints > 0 ? (int)Math.Floor(MaxStopPoints / TickSize + 1e-9) : 0;
                core.SkipFomcDays = SkipFomcDays;
                core.SkipQqqCallFrenzy = SkipQqqCallFrenzy;
                core.QqqCallFrenzyMaxPct = QqqCallFrenzyMaxPct;
                core.SkipVix9dInverted = SkipVix9dInverted;
                core.MaxVix9dRatio = MaxVix9dRatio;
                core.SetExtraFomcDates(ExtraFomcDates);
                core.RegimeLookup = delegate(DateTime d)
                {
                    if (State == State.Realtime) regime.ReloadIfChanged();
                    return regime.Get(d);
                };

                sessionIterator = new SessionIterator(BarsArray[0]);
                if (WriteCsv && !optimizing) OpenCsv();

                Say(string.Format(CultureInfo.InvariantCulture,
                    "{0}, ES ref {1}, qty {2}. Check {3} ET, flat {4} ET. Stop max({5}, round({6} x ATR15)){7}. SkipFOMC={8}. CSV={9} ({10} rows). DLL {11}.",
                    Instrument.FullName, BarsArray[EsIndex].Instrument.FullName, Quantity, Hhmm(checkMinute), Hhmm(exitMinute),
                    MinStopTicks, StopAtrMultiple, MaxStopPoints > 0 ? string.Format(CultureInfo.InvariantCulture, ", cap {0} pts", MaxStopPoints) : "",
                    SkipFomcDays ? "on" : "off", resolvedCsvPath, regime.Count,
                    MaxDailyLossDollars > 0 ? "$" + MaxDailyLossDollars.ToString("0.##", CultureInfo.InvariantCulture) : "off"));
            }
            else if (State == State.Realtime)
            {
                if (refused) return;
                if (regime != null) regime.ReloadIfChanged();
                if (todaySignal != null && entryDay == todaySignal.Date && todaySignal.Date == railDay)
                    Say("Today's signal occurred on historical bars. No live entry will be sent today.");
                if (Position.MarketPosition != MarketPosition.Flat)
                    Say("A virtual historical position is open. StartBehavior WaitUntilFlat prevents live orders until flat.");
                StartClock();
            }
            else if (State == State.Terminated)
            {
                StopClock();
                if (core != null && core.StudyDays > 0)
                    Say(string.Format("{0} valid days, {1} signals, {2} entries placed, {3} trades written.",
                        core.StudyDays, core.SignalDays, signalsPlaced, tradesLogged));
                if (writer != null) { writer.Flush(); writer.Dispose(); writer = null; }
            }
        }

        private string StartProblem()
        {
            BarsPeriod bp = BarsArray[0].BarsPeriod;
            if (bp.BarsPeriodType != BarsPeriodType.Minute || bp.Value != 1)
                return "Primary series must be 1 Minute bars (found " + bp.ToString() + ").";
            string root = Instrument.MasterInstrument.Name;
            if (root != "MNQ" && root != "NQ" && !AllowOtherInstruments)
                return "Validated on MNQ (NQ allowed). Found " + Instrument.FullName + ". Enable 'Allow other instruments' to override.";
            if (BarsArray.Length < 3) return "ES reference series did not load.";
            checkMinute = ToMinute(CheckTimeEt);
            exitMinute = ToMinute(ExitTimeEt);
            if (checkMinute < 0 || exitMinute < 0) return "Times must be HHMM with minutes 00 to 59.";
            if (checkMinute < 12 * 60 || checkMinute > 15 * 60) return "Check time must be between 1200 and 1500 ET.";
            if (exitMinute <= checkMinute + 5 || exitMinute > 15 * 60 + 55) return "Exit time must be after check time and at or before 1555 ET.";
            return null;
        }

        protected override void OnBarUpdate()
        {
            if (refused || core == null) return;
            if (CurrentBars[BarsInProgress] < 0) return;
            if (BarsInProgress == TickIndex) { OnTrade(); return; }
            if (!runWritten && BarsInProgress == 0) { runWritten = true; WriteRunRow(); }

            int bip = BarsInProgress;
            DateTime end = ToEastern(Times[bip][0]);
            double ts = BarsArray[bip].Instrument.MasterInstrument.TickSize;
            if (bip == EsIndex)
            {
                core.OnEsBar(end, ToTicks(Opens[EsIndex][0], ts), ToTicks(Closes[EsIndex][0], ts));
                return;
            }
            if (bip != 0) return;

            NewDay(end.Date);
            core.OnBar(end, ToTicks(Opens[0][0], ts), ToTicks(Highs[0][0], ts), ToTicks(Lows[0][0], ts), ToTicks(Closes[0][0], ts), Volumes[0][0]);
            for (int i = 0; i < core.Events.Count; i++)
            {
                LgDecision ev = core.Events[i];
                if (ev.Kind == "decision") OnDecision(ev, end);
                else if (ev.Kind == "flat") Flatten(FlatName, end, "flat at " + Hhmm(exitMinute) + " ET");
            }
            core.Events.Clear();
        }

        private void OnTrade()
        {
            DateTime t = ToEastern(Times[TickIndex][0]);
            lastTradeEt = t;
            NewDay(t.Date);
            if (Position.MarketPosition == MarketPosition.Flat) return;
            string outside = OutsideHoldWindow(t);
            if (outside != null) { Flatten(SafetyName, t, outside); return; }
            if (MaxDailyLossDollars > 0 && !haltedToday)
            {
                double open = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Closes[TickIndex][0]) - openCommission;
                double total = realizedToday + open;
                if (total <= -MaxDailyLossDollars)
                {
                    haltedToday = true;
                    string note = string.Format(CultureInfo.InvariantCulture, "daily P&L ${0:0.00} hit ${1:0.00} limit at {2}", total, MaxDailyLossDollars, F(Closes[TickIndex][0]));
                    Rail(t, "daily loss limit", note);
                    Flatten(DailyLossName, t, note);
                }
            }
        }

        private void OnDecision(LgDecision ev, DateTime barEnd)
        {
            string head = string.Format("{0} {1} ET", D(ev.Date), barEnd.ToString("HH:mm", CultureInfo.InvariantCulture));
            if (ev.Action != "signal")
            {
                WriteDecisionRow(barEnd, ev, ev.Action, ev.Reason);
                return;
            }
            todaySignal = ev;
            DateTime now = State == State.Realtime && lastTradeEt > barEnd ? lastTradeEt : barEnd;
            string block = EntryBlock(ev, now);
            if (block != null)
            {
                WriteDecisionRow(barEnd, ev, "blocked", block);
                Rail(now, "entry refused", block);
                Say(head + ": SIGNAL LONG blocked by rail: " + block + ".");
                return;
            }
            WriteDecisionRow(barEnd, ev, "signal", "");
            Say(string.Format(CultureInfo.InvariantCulture,
                "{0}: SIGNAL LONG (Positive Gamma). Close {1} > VWAP {2}, ES Open {3} > Flip {4:0.00}, ATR15 {5:0.0}t, stop {6}t ({7} pts).",
                head, F(ev.CloseTicks * TickSize), F(ev.VwapTicks * TickSize), F(ev.EsOpenTicks * BarsArray[EsIndex].Instrument.MasterInstrument.TickSize),
                ev.MappedFlipTicks * BarsArray[EsIndex].Instrument.MasterInstrument.TickSize, ev.Atr15Ticks, ev.StopTicks, F(ev.StopTicks * TickSize)));

            SetStopLoss(LongName, CalculationMode.Ticks, ev.StopTicks, false);
            entryDay = ev.Date;
            openStopTicks = ev.StopTicks;
            signalsPlaced++;
            openNote = string.Format(CultureInfo.InvariantCulture, "PosGamma; ATR15 {0:0.0}t", ev.Atr15Ticks);
            Row("kind", "order", "date", D(ev.Date), "time_et", T(now), "action", "enter long", "dir", "1",
                "stop_ticks", ev.StopTicks.ToString(CultureInfo.InvariantCulture), "qty", Quantity.ToString(CultureInfo.InvariantCulture), "note", openNote);
            EnterLong(TickIndex, Quantity, LongName);
        }

        private string EntryBlock(LgDecision ev, DateTime nowEt)
        {
            if (haltedToday || (MaxDailyLossDollars > 0 && realizedToday <= -MaxDailyLossDollars)) return "daily loss limit reached today";
            if (entryDay == ev.Date) return "already entered once today";
            if (Position.MarketPosition != MarketPosition.Flat) return "position already open";
            if (State == State.Realtime && PositionAccount != null && PositionAccount.MarketPosition != MarketPosition.Flat)
            {
                if (!AllowSharedAccountPositions) return "account already holds a position in " + Instrument.FullName;
                if (BlockOpposingAccountPosition && ev.Dir > 0 && PositionAccount.MarketPosition == MarketPosition.Short)
                    return "account holds an opposing short position in " + Instrument.FullName;
            }
            if (nowEt.Date != ev.Date) return "decision date mismatch";
            TimeSpan tod = nowEt.TimeOfDay;
            if (tod < TimeSpan.FromMinutes(checkMinute - 5) || tod > TimeSpan.FromMinutes(checkMinute + 10)) return "outside check window";
            if (tod >= TimeSpan.FromMinutes(exitMinute)) return "at or after exit time";
            if (ev.StopTicks <= 0) return "invalid stop distance";
            return EarlyClose(ev.Date);
        }

        private string EarlyClose(DateTime day)
        {
            if (sessionIterator == null) return null;
            try
            {
                sessionIterator.GetNextSession(Times[0][0], true);
                DateTime endEt = ToEastern(sessionIterator.ActualSessionEnd);
                if (endEt.Date == day && endEt.TimeOfDay < new TimeSpan(16, 0, 0))
                    return "early close day (ends " + endEt.ToString("HH:mm", CultureInfo.InvariantCulture) + " ET)";
            }
            catch (Exception ex) { Say("session check warning: " + ex.Message); }
            return null;
        }

        private string OutsideHoldWindow(DateTime tEt)
        {
            if (tEt.TimeOfDay >= HardFlat) return "position open at or after 15:56 ET";
            if (tEt.TimeOfDay < TimeSpan.FromMinutes(checkMinute - 10) || tEt.Date != entryDay) return "position open outside hold window";
            return null;
        }

        private void Flatten(string name, DateTime tEt, string why)
        {
            if (Position.MarketPosition == MarketPosition.Flat || exitPending) return;
            exitPending = true; exitPendingName = name;
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            Say(string.Format("{0} ET: FLATTEN {1} {2} ({3}).", T(tEt), isLong ? "long" : "short", Position.Quantity, why));
            Row("kind", "order", "date", D(tEt), "time_et", T(tEt), "action", "exit " + name, "note", why);
            if (isLong) ExitLong(TickIndex, Position.Quantity, name, LongName);
            else ExitShort(TickIndex, Position.Quantity, name, "");
        }

        private void NewDay(DateTime day) { if (day != railDay) { railDay = day; haltedToday = false; realizedToday = 0; } }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            if (orderState != OrderState.Rejected && orderState != OrderState.Cancelled) return;
            if (order.Name == exitPendingName) exitPending = false;
            if (orderState != OrderState.Rejected) return;
            DateTime t = ToEastern(time);
            Rail(t, "order rejected", order.Name + ": " + nativeError);
            Say(string.Format("{0} ET: ORDER REJECTED {1}: {2}", T(t), order.Name, nativeError));
            if (order.Name == "Stop loss" && Position.MarketPosition != MarketPosition.Flat)
            {
                haltedToday = true;
                Flatten(SafetyName, t, "protective stop rejected");
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order == null) return;
            string name = execution.Order.Name;
            DateTime t = ToEastern(execution.Time);
            int q = execution.Quantity;
            Row("kind", "fill", "date", D(t), "time_et", T(t), "action", name, "price", F(execution.Price),
                "dir", execution.MarketPosition == MarketPosition.Long ? "buy" : "sell", "commission", F(execution.Commission),
                "qty", q.ToString(CultureInfo.InvariantCulture));
            if (name == LongName)
            {
                if (openQty == 0) { openTime = t; openValue = openCommission = exitValue = exitCommission = 0; closedQty = 0; }
                openQty += q; openValue += execution.Price * q; openCommission += execution.Commission;
                double avg = openValue / openQty;
                Say(string.Format(CultureInfo.InvariantCulture, "{0} ET: filled long {1} at {2}. Stop {3} ({4} ticks).",
                    T(t), openQty, F(avg), F(avg - openStopTicks * TickSize), openStopTicks));
                return;
            }
            if (openQty <= 0) return;
            closedQty += q; exitValue += execution.Price * q; exitCommission += execution.Commission;
            lastExitTime = t; lastExitName = name;
            if (closedQty < openQty) return;
            WriteTradeRow();
            openQty = closedQty = 0; exitPending = false;
        }

        #region Clock, helpers, CSV

        private void StartClock()
        {
            if (clock != null || (Account != null && Account.Name == "Playback101")) return;
            clock = new System.Timers.Timer(5000); clock.AutoReset = true; clock.Elapsed += OnClock; clock.Start();
        }
        private void StopClock() { if (clock == null) return; clock.Stop(); clock.Elapsed -= OnClock; clock.Dispose(); clock = null; }
        private void OnClock(object s, System.Timers.ElapsedEventArgs e) { try { TriggerCustomEvent(ClockCheck, 0, null); } catch { } }
        private void ClockCheck(object st)
        {
            if (State != State.Realtime || Position.MarketPosition == MarketPosition.Flat) return;
            DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern);
            string outside = OutsideHoldWindow(now);
            if (outside != null) Flatten(SafetyName, now, "clock check: " + outside);
        }

        private static bool IsAuto(string s) { return string.IsNullOrWhiteSpace(s) || s.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase); }
        private string ContractSuffix()
        {
            if (Instrument == null) return "##-##";
            string full = Instrument.FullName ?? "";
            int sp = full.IndexOf(' ');
            return sp > 0 && sp + 1 < full.Length ? full.Substring(sp + 1) : "##-##";
        }
        private static bool InstrumentExists(string name) { if (name.Contains("##")) return true; try { return NinjaTrader.Cbi.Instrument.GetInstrument(name) != null; } catch { return false; } }
        private static long ToTicks(double price, double ts) { return (long)Math.Round(price / ts); }
        private DateTime ToEastern(DateTime t)
        {
            DateTime key = new DateTime(t.Year, t.Month, t.Day, t.Hour, 0, 0);
            if (key != offsetKey)
            {
                offsetKey = key;
                try { offsetToEastern = TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(key, DateTimeKind.Unspecified), NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo, eastern) - key; }
                catch { offsetToEastern = TimeSpan.Zero; }
            }
            return t + offsetToEastern;
        }
        private static int ToMinute(int hhmm) { int h = hhmm / 100, m = hhmm % 100; return (hhmm < 0 || m > 59 || h > 23) ? -1 : h * 60 + m; }
        private static string Hhmm(int minute) { return string.Format("{0:00}:{1:00}", minute / 60, minute % 60); }
        private void Say(string text) { if (!optimizing) Print("LongGammaDriftOpenSource: " + text); }
        private void Rail(DateTime tEt, string what, string note) { Row("kind", "rail", "date", D(tEt), "time_et", T(tEt), "action", what, "note", note); }
        private static string ExitReason(string n) { return n == FlatName ? "flat 15:55" : n == "Stop loss" ? "stop" : n == SafetyName ? "safety flat" : n == DailyLossName ? "daily loss" : n; }
        private static string Csv(string s) { return (s ?? "").Replace(',', ';').Replace('\n', ' ').Replace('\r', ' '); }

        private static readonly string[] Columns = { "kind", "date", "time_et", "action", "dir", "price", "vwap", "atr15_ticks",
            "stop_ticks", "qty", "entry_time_et", "entry_price", "exit_time_et", "exit_price", "exit_reason", "gross_ticks", "commission", "pnl", "note" };

        private void Row(params string[] pairs)
        {
            if (writer == null) return;
            string[] cells = new string[Columns.Length];
            for (int i = 0; i + 1 < pairs.Length; i += 2) { int k = Array.IndexOf(Columns, pairs[i]); if (k >= 0) cells[k] = Csv(pairs[i + 1]); }
            writer.WriteLine(string.Join(",", cells));
        }
        private static string F(double x) { return double.IsNaN(x) ? "" : x.ToString("0.##########", CultureInfo.InvariantCulture); }
        private static string D(DateTime t) { return t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        private static string T(DateTime t) { return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }

        private void OpenCsv()
        {
            try
            {
                string dir = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "OpenSourceEdge", "Logs");
                Directory.CreateDirectory(dir);
                string inst = Instrument.FullName;
                foreach (char ch in Path.GetInvalidFileNameChars()) inst = inst.Replace(ch, '_');
                string file = Path.Combine(dir, string.Format("longgamma_{0}_{1:yyyyMMdd_HHmmss}.csv", inst.Replace(' ', '_'), DateTime.Now));
                writer = new StreamWriter(file, false);
                writer.WriteLine(string.Join(",", Columns));
                Say("log file " + file);
            }
            catch (Exception ex) { writer = null; Say("could not open CSV: " + ex.Message); }
        }

        private void WriteRunRow()
        {
            if (BarsArray[TickIndex].Count == 0) Say("WARNING: 1-tick series has no data.");
            if (BarsArray[EsIndex].Count == 0) Say("WARNING: ES reference series " + esName + " has no data.");
            if (writer == null) return;
            Row("kind", "run", "note", string.Format(CultureInfo.InvariantCulture,
                "instrument={0};es={1};qty={2};check={3};exit={4};stop_mult={5};min_stop={6};max_stop_pts={7};skip_fomc={8};csv={9};dll={10}",
                Instrument.FullName, BarsArray[EsIndex].Instrument.FullName, Quantity, Hhmm(checkMinute), Hhmm(exitMinute),
                StopAtrMultiple, MinStopTicks, MaxStopPoints, SkipFomcDays ? 1 : 0, resolvedCsvPath, MaxDailyLossDollars));
        }

        private void WriteDecisionRow(DateTime barEnd, LgDecision ev, string action, string note)
        {
            if (writer == null) return;
            Row("kind", "decision", "date", D(ev.Date), "time_et", T(barEnd), "action", action, "dir", ev.Dir.ToString(CultureInfo.InvariantCulture),
                "price", ev.CloseTicks > 0 ? F(ev.CloseTicks * TickSize) : "",
                "vwap", !double.IsNaN(ev.VwapTicks) ? F(ev.VwapTicks * TickSize) : "",
                "atr15_ticks", ev.Atr15Ticks > 0 ? F(ev.Atr15Ticks) : "",
                "stop_ticks", ev.StopTicks > 0 ? ev.StopTicks.ToString(CultureInfo.InvariantCulture) : "", "note", note);
        }

        private void WriteTradeRow()
        {
            tradesLogged++;
            double entry = openValue / openQty, exit = exitValue / closedQty;
            double grossTicks = (exit - entry) * openQty / TickSize;
            double commission = openCommission + exitCommission;
            double pnl = (exitValue - openValue) * Instrument.MasterInstrument.PointValue - commission;
            realizedToday += pnl;
            Say(string.Format(CultureInfo.InvariantCulture, "{0} ET: closed long {1} ({2}): {3} contract-ticks, ${4:0.00} after ${5:0.00} commission.",
                T(lastExitTime), openQty, ExitReason(lastExitName), F(Math.Round(grossTicks, 2)), pnl, commission));
            if (writer == null) return;
            Row("kind", "trade", "date", D(openTime), "dir", "long", "qty", openQty.ToString(CultureInfo.InvariantCulture),
                "entry_time_et", T(openTime), "entry_price", F(entry), "stop_ticks", openStopTicks.ToString(CultureInfo.InvariantCulture),
                "exit_time_et", T(lastExitTime), "exit_price", F(exit), "exit_reason", ExitReason(lastExitName),
                "gross_ticks", F(Math.Round(grossTicks, 4)), "commission", F(commission),
                "pnl", pnl.ToString("0.00", CultureInfo.InvariantCulture), "note", openNote);
            writer.Flush();
        }

        #endregion

        #region Properties

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name = "Quantity", Order = 1, GroupName = "1. Signal")]
        public int Quantity { get; set; }

        [NinjaScriptProperty, Range(1200, 1500)]
        [Display(Name = "Check time ET (HHMM)", Description = "1300 is the research default (plateau 1230 to 1315).", Order = 2, GroupName = "1. Signal")]
        public int CheckTimeEt { get; set; }

        [NinjaScriptProperty, Range(1305, 1555)]
        [Display(Name = "Exit time ET (HHMM)", Order = 1, GroupName = "2. Stop and Exits")]
        public int ExitTimeEt { get; set; }

        [NinjaScriptProperty, Range(0.5, 10.0)]
        [Display(Name = "Stop (ATR15 multiple)", Order = 2, GroupName = "2. Stop and Exits")]
        public double StopAtrMultiple { get; set; }

        [NinjaScriptProperty, Range(4, 200)]
        [Display(Name = "Min stop (ticks)", Order = 3, GroupName = "2. Stop and Exits")]
        public int MinStopTicks { get; set; }

        [NinjaScriptProperty, Range(0, 100000)]
        [Display(Name = "Max stop (points)", Order = 4, GroupName = "2. Stop and Exits")]
        public double MaxStopPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip FOMC days", Description = "Pause on scheduled Fed FOMC decision days.", Order = 1, GroupName = "3. Regime and Filters")]
        public bool SkipFomcDays { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip QQQ call frenzy (optional CSV)", Description = "Skips entry if qqq_pcvol_p60 <= threshold. Ignored if column absent.", Order = 2, GroupName = "3. Regime and Filters")]
        public bool SkipQqqCallFrenzy { get; set; }

        [NinjaScriptProperty, Range(0.05, 0.50)]
        [Display(Name = "QQQ call frenzy max pct", Order = 3, GroupName = "3. Regime and Filters")]
        public double QqqCallFrenzyMaxPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Skip VIX9D/VIX >= 1.05 (optional CSV)", Description = "Skips entry when short-term volatility is inverted. Ignored if column absent.", Order = 4, GroupName = "3. Regime and Filters")]
        public bool SkipVix9dInverted { get; set; }

        [NinjaScriptProperty, Range(0.80, 2.00)]
        [Display(Name = "VIX9D/VIX veto threshold", Order = 5, GroupName = "3. Regime and Filters")]
        public double MaxVix9dRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Regime CSV path", Description = "Blank uses Documents\\NinjaTrader 8\\OpenSourceEdge\\daily_regime.csv.", Order = 6, GroupName = "3. Regime and Filters")]
        public string RegimeCsvPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ES instrument", Description = "Blank or auto matches the chart contract month (e.g. ES 12-26).", Order = 7, GroupName = "3. Regime and Filters")]
        public string EsInstrument { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Extra FOMC dates (YYYY-MM-DD)", Order = 8, GroupName = "3. Regime and Filters")]
        public string ExtraFomcDates { get; set; }

        [NinjaScriptProperty, Range(0, 100000)]
        [Display(Name = "Max daily loss ($)", Order = 1, GroupName = "4. Safety")]
        public double MaxDailyLossDollars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow other instruments", Order = 2, GroupName = "4. Safety")]
        public bool AllowOtherInstruments { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow shared account positions", Order = 3, GroupName = "4. Safety")]
        public bool AllowSharedAccountPositions { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Block opposing account position", Order = 4, GroupName = "4. Safety")]
        public bool BlockOpposingAccountPosition { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Write CSV log", Order = 1, GroupName = "5. Output")]
        public bool WriteCsv { get; set; }

        #endregion
    }
}

// CORE-BEGIN
namespace NinjaTrader.NinjaScript.Strategies.OpenSourceEdgeLgCore
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    public sealed class LgRegimeRow
    {
        public double SpotD1 = double.NaN;
        public double D14GammaFlip = double.NaN;
        public double AllGammaFlip = double.NaN;
        public double D14NetGexB = double.NaN;
        public double QqqPcVolP60 = double.NaN;
        public double Vix9dVix = double.NaN;
        public bool IsFomc;
    }

    public sealed class LgRegimeTable
    {
        private readonly string path;
        private DateTime lastWrite = DateTime.MinValue;
        private readonly Dictionary<DateTime, LgRegimeRow> rows = new Dictionary<DateTime, LgRegimeRow>();

        public LgRegimeTable(string csvPath) { path = csvPath ?? ""; }
        public int Count { get { return rows.Count; } }

        public void ReloadIfChanged()
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                DateTime wt = File.GetLastWriteTimeUtc(path);
                if (wt == lastWrite && rows.Count > 0) return;
                lastWrite = wt;
                rows.Clear();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string header = sr.ReadLine();
                    if (header == null) return;
                    string[] cols = header.Split(',');
                    int iDate = Col(cols, "date"), iSpot = Col(cols, "spot_d1"), iGf14 = Col(cols, "d14_gamma_flip"),
                        iGfa = Col(cols, "all_gamma_flip"), iNg14 = Col(cols, "d14_net_gex_b"),
                        iQqq = Col(cols, "qqq_pcvol_p60"), iV9 = Col(cols, "vix9d_vix"), iFomc = Col(cols, "is_fomc");
                    if (iDate < 0) return;
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        string[] f = line.Split(',');
                        DateTime d;
                        if (iDate >= f.Length || !DateTime.TryParseExact(f[iDate].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                        LgRegimeRow r = new LgRegimeRow();
                        r.SpotD1 = ParseD(f, iSpot);
                        r.D14GammaFlip = ParseD(f, iGf14);
                        r.AllGammaFlip = ParseD(f, iGfa);
                        r.D14NetGexB = ParseD(f, iNg14);
                        r.QqqPcVolP60 = ParseD(f, iQqq);
                        r.Vix9dVix = ParseD(f, iV9);
                        r.IsFomc = iFomc >= 0 && iFomc < f.Length && f[iFomc].Trim() == "1";
                        rows[d.Date] = r;
                    }
                }
            }
            catch { }
        }

        public LgRegimeRow Get(DateTime day) { LgRegimeRow r; return rows.TryGetValue(day.Date, out r) ? r : null; }

        private static int Col(string[] cols, string name)
        {
            for (int i = 0; i < cols.Length; i++) if (cols[i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
        private static double ParseD(string[] f, int idx)
        {
            double v;
            if (idx < 0 || idx >= f.Length) return double.NaN;
            return double.TryParse(f[idx].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }
    }

    public sealed class LgAtr15
    {
        private readonly Queue<long> trs = new Queue<long>();
        private long trSum, prevC, curBucket, bh, bl, bc;
        private bool hasPrevC, hasBucket;

        public double Value { get { return trs.Count >= 5 ? (double)trSum / trs.Count : 0.0; } }
        private void PushTr(long tr) { trs.Enqueue(tr); trSum += tr; if (trs.Count > 14) trSum -= trs.Dequeue(); }

        public void FlushSession()
        {
            if (hasBucket)
            {
                PushTr(hasPrevC ? Math.Max(bh - bl, Math.Max(Math.Abs(bh - prevC), Math.Abs(bl - prevC))) : (bh - bl));
                prevC = bc; hasPrevC = true; hasBucket = false;
            }
        }

        public double Push(int etMin, long h, long l, long c)
        {
            int b = (int)Math.Floor((etMin - 1) / 15.0);
            if (!hasBucket) { hasBucket = true; curBucket = b; bh = h; bl = l; bc = c; }
            else if (b != curBucket)
            {
                PushTr(hasPrevC ? Math.Max(bh - bl, Math.Max(Math.Abs(bh - prevC), Math.Abs(bl - prevC))) : (bh - bl));
                prevC = bc; hasPrevC = true; curBucket = b; bh = h; bl = l; bc = c;
            }
            else { if (h > bh) bh = h; if (l < bl) bl = l; bc = c; }
            if (etMin % 15 == 0)
            {
                PushTr(hasPrevC ? Math.Max(bh - bl, Math.Max(Math.Abs(bh - prevC), Math.Abs(bl - prevC))) : (bh - bl));
                prevC = bc; hasPrevC = true; hasBucket = false;
            }
            return Value;
        }
    }

    public sealed class LgDecision
    {
        public string Kind = "decision";
        public DateTime Date;
        public string Action = "";
        public string Reason = "";
        public int Dir;
        public long CloseTicks, EsOpenTicks;
        public double MappedFlipTicks = double.NaN;
        public double VwapTicks = double.NaN;
        public double Atr15Ticks;
        public int StopTicks;
    }

    public sealed class LgCore
    {
        public const int RthOpen = 9 * 60 + 30;
        public const int RthClose = 16 * 60;
        public const int MinRthBars = 331;

        private static readonly HashSet<DateTime> BuiltinFomc = BuildFomcSet();

        private static HashSet<DateTime> BuildFomcSet()
        {
            string[] raw = {
                "2019-01-30","2019-03-20","2019-05-01","2019-06-19","2019-07-31","2019-09-18","2019-10-30","2019-12-11",
                "2020-01-29","2020-04-29","2020-06-10","2020-07-29","2020-09-16","2020-11-05","2020-12-16",
                "2021-01-27","2021-03-17","2021-04-28","2021-06-16","2021-07-28","2021-09-22","2021-11-03","2021-12-15",
                "2022-01-26","2022-03-16","2022-05-04","2022-06-15","2022-07-27","2022-09-21","2022-11-02","2022-12-14",
                "2023-02-01","2023-03-22","2023-05-03","2023-06-14","2023-07-26","2023-09-20","2023-11-01","2023-12-13",
                "2024-01-31","2024-03-20","2024-05-01","2024-06-12","2024-07-31","2024-09-18","2024-11-07","2024-12-18",
                "2025-01-29","2025-03-19","2025-05-07","2025-06-18","2025-07-30","2025-09-17","2025-10-29","2025-12-10",
                "2026-01-28","2026-03-18","2026-04-29","2026-06-17","2026-07-29","2026-09-16","2026-10-28","2026-12-09"
            };
            HashSet<DateTime> s = new HashSet<DateTime>();
            for (int i = 0; i < raw.Length; i++) s.Add(DateTime.ParseExact(raw[i], "yyyy-MM-dd", CultureInfo.InvariantCulture));
            return s;
        }

        public int CheckMinute = 13 * 60;
        public int MaxCheckLag = 3;
        public int ExitMinute = 15 * 60 + 55;
        public double StopAtrMult = 2.0;
        public int MinStopTicks = 16;
        public int MaxStopTicks;
        public bool SkipFomcDays = true;
        public bool SkipQqqCallFrenzy;
        public double QqqCallFrenzyMaxPct = 0.15;
        public bool SkipVix9dInverted;
        public double MaxVix9dRatio = 1.05;
        public Func<DateTime, LgRegimeRow> RegimeLookup;

        public readonly List<LgDecision> Events = new List<LgDecision>();
        public DateTime Day = DateTime.MinValue;
        public int StudyDays, SignalDays;

        private readonly LgAtr15 atr15 = new LgAtr15();
        private readonly HashSet<DateTime> extraFomc = new HashSet<DateTime>();

        private DateTime esDay = DateTime.MinValue;
        private int esRthBars;
        private bool esHasOpen, esHasClose, hasEsPdc;
        private long esOpen930, esLastRthClose, esPdc;

        private int rthBars, checkMin;
        private bool hasOpen, hasClose, decided, flatDone, hasCheck;
        private long checkClose;
        private double checkVwap = double.NaN, checkAtr;
        private double cumPv, cumV;

        public void SetExtraFomcDates(string csvDates)
        {
            extraFomc.Clear();
            if (string.IsNullOrWhiteSpace(csvDates)) return;
            string[] parts = csvDates.Split(new[] { ',', ';', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                DateTime d;
                if (DateTime.TryParseExact(parts[i].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    extraFomc.Add(d.Date);
            }
        }

        public static DateTime SessionDate(DateTime endEt) { return endEt.AddMinutes(-1).AddHours(6).Date; }
        public static int MinuteOf(DateTime endEt, DateTime day) { return (int)Math.Round((endEt - day).TotalMinutes); }

        public void OnEsBar(DateTime endEt, long o, long c)
        {
            DateTime sd = SessionDate(endEt);
            if (sd.DayOfWeek == DayOfWeek.Saturday || sd.DayOfWeek == DayOfWeek.Sunday) return;
            if (sd < esDay) return;
            if (sd > esDay)
            {
                if (esDay != DateTime.MinValue && esRthBars >= MinRthBars && esHasOpen && esHasClose)
                {
                    esPdc = esLastRthClose;
                    hasEsPdc = true;
                }
                esDay = sd;
                esRthBars = 0;
                esHasOpen = esHasClose = false;
                esOpen930 = esLastRthClose = 0;
            }
            int e = MinuteOf(endEt, sd);
            if (e > RthOpen && e <= RthClose)
            {
                if (esRthBars == 0) esOpen930 = o;
                esRthBars++;
                if (e >= RthOpen + 1 && e <= RthOpen + 5) esHasOpen = true;
                if (e >= RthClose - 5 && e <= RthClose) esHasClose = true;
                esLastRthClose = c;
            }
        }

        public void OnBar(DateTime endEt, long o, long h, long l, long c, double volume)
        {
            Events.Clear();
            DateTime sd = SessionDate(endEt);
            if (sd.DayOfWeek == DayOfWeek.Saturday || sd.DayOfWeek == DayOfWeek.Sunday) return;
            if (sd < Day) return;
            if (sd > Day)
            {
                FinishDay();
                Day = sd;
                rthBars = 0;
                hasOpen = hasClose = decided = flatDone = hasCheck = false;
                checkMin = int.MinValue;
                checkClose = 0; checkVwap = double.NaN; checkAtr = 0;
                cumPv = cumV = 0;
            }

            int e = MinuteOf(endEt, sd);
            if (e > 16 * 60 + 15) return;

            double a15 = atr15.Push(e, h, l, c);
            if (e <= RthOpen) return;

            if (!decided && e > CheckMinute) Decide();
            if (!flatDone && e >= ExitMinute)
            {
                flatDone = true;
                LgDecision fl = new LgDecision();
                fl.Kind = "flat";
                fl.Date = Day;
                Events.Add(fl);
            }

            if (e <= RthClose)
            {
                rthBars++;
                if (e >= RthOpen + 1 && e <= RthOpen + 5) hasOpen = true;
                if (e >= RthClose - 5 && e <= RthClose) hasClose = true;

                double tp = ((double)h + (double)l + (double)c) / 3.0;
                double v = volume > 0 ? volume : 0.0;
                cumPv += tp * v;
                cumV += v;
                double vwap = cumV > 0 ? cumPv / cumV : tp;

                if (e <= CheckMinute && e >= CheckMinute - MaxCheckLag)
                {
                    checkMin = e;
                    checkClose = c;
                    checkVwap = vwap;
                    checkAtr = a15;
                    hasCheck = true;
                }
                if (!decided && e == CheckMinute) Decide();
            }
        }

        private void Decide()
        {
            decided = true;
            LgDecision ev = new LgDecision();
            ev.Kind = "decision";
            ev.Date = Day;
            LgRegimeRow row = RegimeLookup != null ? RegimeLookup(Day) : null;

            if (SkipFomcDays && (BuiltinFomc.Contains(Day) || extraFomc.Contains(Day) || (row != null && row.IsFomc)))
            {
                ev.Action = "skip"; ev.Reason = "FOMC decision day";
                Events.Add(ev); return;
            }
            if (row == null)
            {
                ev.Action = "skip"; ev.Reason = "no regime row in CSV for today";
                Events.Add(ev); return;
            }
            if (!hasEsPdc || esDay != Day || esOpen930 <= 0)
            {
                ev.Action = "skip"; ev.Reason = "missing ES prior close or 09:30 open";
                Events.Add(ev); return;
            }
            double mappedFlip = double.NaN;
            if (!double.IsNaN(row.SpotD1) && row.SpotD1 > 0)
            {
                if (!double.IsNaN(row.D14GammaFlip) && row.D14GammaFlip > 0)
                    mappedFlip = (double)esPdc * (row.D14GammaFlip / row.SpotD1);
                else if (!double.IsNaN(row.AllGammaFlip) && row.AllGammaFlip > 0)
                    mappedFlip = (double)esPdc * (row.AllGammaFlip / row.SpotD1);
            }
            ev.EsOpenTicks = esOpen930;
            ev.MappedFlipTicks = mappedFlip;
            int reg = !double.IsNaN(mappedFlip) ? ((double)esOpen930 > mappedFlip ? 1 : -1)
                : (!double.IsNaN(row.D14NetGexB) ? (row.D14NetGexB > 0 ? 1 : -1) : 0);
            if (reg != 1)
            {
                ev.Action = "no signal"; ev.Reason = "ES open not above mapped SPX Gamma Flip";
                Events.Add(ev); return;
            }
            if (SkipQqqCallFrenzy && !double.IsNaN(row.QqqPcVolP60) && row.QqqPcVolP60 <= QqqCallFrenzyMaxPct)
            {
                ev.Action = "skip"; ev.Reason = "QQQ call frenzy veto";
                Events.Add(ev); return;
            }
            if (SkipVix9dInverted && !double.IsNaN(row.Vix9dVix) && row.Vix9dVix >= MaxVix9dRatio)
            {
                ev.Action = "skip"; ev.Reason = "VIX9D/VIX inversion veto";
                Events.Add(ev); return;
            }
            if (!hasCheck || checkMin < CheckMinute - MaxCheckLag || checkAtr <= 0 || double.IsNaN(checkVwap))
            {
                ev.Action = "skip"; ev.Reason = "missing 13:00 bar or ATR15 not ready";
                Events.Add(ev); return;
            }
            ev.CloseTicks = checkClose;
            ev.VwapTicks = checkVwap;
            ev.Atr15Ticks = checkAtr;
            if ((double)checkClose <= checkVwap)
            {
                ev.Action = "no signal"; ev.Reason = "13:00 close at or below RTH VWAP";
                Events.Add(ev); return;
            }

            int rawStop = Math.Max(MinStopTicks, (int)Math.Round(StopAtrMult * checkAtr));
            ev.StopTicks = MaxStopTicks > 0 ? Math.Min(rawStop, MaxStopTicks) : rawStop;
            ev.Dir = 1;
            ev.Action = "signal";
            SignalDays++;
            Events.Add(ev);
        }

        private void FinishDay()
        {
            if (Day == DateTime.MinValue) return;
            atr15.FlushSession();
            if (rthBars >= MinRthBars && hasOpen && hasClose) StudyDays++;
        }
    }
}
// CORE-END
