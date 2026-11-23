<p align="center">
  <img src="assets/opensourceedge-banner.png" alt="OpenSourceEdge Banner" width="100%" />
</p>

# OpenSourceEdge

**Stop microing 1-minute candles when you have zero macro.**

Institutional options flows and intraday futures **Build Orders** explained in plain English, verified on real ticks, and open-sourced for `$0`.

- **Core Dataset:** 8 years of futures data (`2019 to 2026`), `1+ billion` real 1-tick fills, and `32 million` SPX/QQQ options rows.
- **Fog of War:** Strictly causal `T-1` signals only. Zero lookahead. No map hacks.
- **Platforms:** NinjaTrader 8 (`C#`) and TradingView (`PineScript v6`).
- **Price:** `$0`. No course. No Discord. No `$99/mo` 6-pool cheese.

Follow releases on X: **[@OpenSourceEdge](https://x.com/OpenSourceEdge)**

---

## Never Used GitHub Before? Start Here (30 Seconds)

You don't need to install Git or touch a terminal.

1. Click any Build Order file below (for example, [`LongGammaDrift_OpenSource.pine`](episodes/ep01-dealer-gamma/LongGammaDrift_OpenSource.pine)).
2. Click the **Copy** icon in the top-right corner of the code box.
3. Paste into **TradingView Pine Editor** (click **Add to chart**) or **NinjaTrader 8 NinjaScript Editor** (press **F5**).

*(Want a simpler page with 1-click Copy buttons? Open our beginner site at `https://open-source-edge.github.io/everything/` once GitHub Pages is live.)*

---

## Verified Build Orders (Free Code)

| Ep | Build Order Codename | Institutional Mechanism | NinjaTrader 8 | TradingView v6 | Guide & Proof |
|---:|---|---|---|---|---|
| **01** | **Shield Battery** *(Protoss)* | **Long-Gamma Afternoon Drift:** Buy `MNQ > VWAP` at `13:00 ET` only when `ES` opens above the pre-market SPX 14-DTE Gamma Flip (`+$9,711` 8y, 7 straight green yrs, `-$1,096` max DD, beat 1m bars on real 1-tick replay) | [`.cs`](episodes/ep01-dealer-gamma/LongGammaDrift_OpenSource.cs) | [`.pine`](episodes/ep01-dealer-gamma/LongGammaDrift_OpenSource.pine) | [Episode 01](episodes/ep01-dealer-gamma/README.md) |

*Upcoming Build Orders queued for release: **Stimpack** (First-5m One-Way Open), **Siege Tank** (Noise Band Breakout), **Blink Stalker** (Fast Morning MNQ-YM Scalp), **Yamato Cannon** (13:10 Treasury Post-Auction), **MULE Drop** (Turn-of-Month Flow), **Psionic Storm** (Short-Gamma Last Hour), and **Widow Mine** (QQQ Call-Euphoria Short).*

---

## The Strategy Graveyard (Fallen Units & 6-Pool Cheese)

Most retail trading folklore dies the second it gets scouted by 1-tick fills, realistic slippage, and blind out-of-sample years. Browse everything that tapped out with `GG` before risking `$1`:

- Read **[GRAVEYARD.md](GRAVEYARD.md)** (ICT/SMC, 1B+ ticks of CVD/Order Flow, 0DTE regime breaks, 9-market friction math, and our own caught map-hack lookahead bugs).

---

## Copy-Paste AI Quant Prompt (Gemini 4 Neon)

Use this prompt to stress-test your own build orders or translate any strategy in this repo into Python, Sierra Chart, TradeStation, or QuantConnect:

```markdown
You are my ruthless Institutional Quantitative Research Partner. Your job isn't to curve-fit a pretty equity curve. Your job is to protect my capital by trying to kill every trading idea I give you, and only keeping what survives strict validation.

### NON-NEGOTIABLE RESEARCH PROTOCOL
Before writing strategy code, enforce these 7 rules:

1. Mechanism First (Macro > Micro):
Force me to state who is forced to buy or sell and why (dealer gamma hedging, month-end pension rebalancing, leveraged-ETF 14:00 rebalance, Treasury auction spillover, volatility-target re-leveraging). Reject vague chart folklore unless framed as a falsifiable hypothesis.

2. Strict 3-Way Temporal Split:
- DEV: Build and freeze rules here only.
- Locked OOS: Run once after freezing. Never tweak parameters based on OOS.
- Virgin Holdout (Most Recent Year): Final untouched sanity check.

3. Zero Lookahead / No Map Hacks:
Every regime filter, volatility metric, gamma level, or cross-market signal must use strictly prior-close (T-1) or already-closed bar timestamps. Audit explicitly for subtle leaks.

4. Real Microstructure Friction:
Include realistic commissions plus both 1 tick and 2 ticks of slippage per side. Flag if 1-tick friction plus commissions exceeds 5% of median stop distance. Reject bar-guess fills and require 1-tick or causal next-bar-open execution.

5. Falsification Suite:
- Control Baseline: Beat the unfiltered baseline.
- Opposite-Direction Placebo: Flip the signal condition. It must degrade or turn negative.
- Parameter Plateau (-33% / Base / +33%): Neighboring thresholds must stay profitable. Kill parameter cliffs.
- Outlier Check: Strip the top 3 best days. If the edge disappears, reject it.

6. Prop-Firm & Tail-Risk Stress Test:
Evaluate worst day, max EOD and intraday drawdown, year-by-year consistency, and correlation to existing strategies. Never assume tight trailing stops, early breakeven stops, or hard daily profit caps help without proving it.

7. Production Dual-Code Output (Only for Survivors):
Generate both NinjaTrader 8 (C# NinjaScript) and TradingView (PineScript v6) with hard stop-loss, session flatten (15:55 ET), and causal timestamps.
```

---

## Disclaimer

Everything in this repository is educational quantitative research and open-source software (MIT License). Nothing here is financial advice. Simulated and historical backtests don't guarantee future live results. Always verify your broker or prop firm's automation rules before enabling live order execution.
