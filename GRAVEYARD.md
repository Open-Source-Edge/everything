# The Strategy Graveyard (Fallen Units & 6-Pool Cheese)

Most trading accounts hide their losses and only post the replays where their 6-pool cheese went unscouted.

Here's everything that tapped out (`GG`) when tested across 8 years of futures data (`2019 to 2026`), `1+ billion` real 1-tick fills, `32 million` SPX/QQQ options rows, realistic commissions, and blind out-of-sample years.

Save yourself months of mining depleted mineral patches.

---

## 1. Retail Folklore That Got Scouted & Crushed

| Strategy / Belief | Test Scope | Cause of Death |
|---|---|---|
| **ICT / SMC Intraday Setups** (`iFVG`, `SMT` divergence, session sweep-and-reclaim, HTF `FVG` pullbacks, lunch fades) | 8 years `MNQ` | Negative or flat **gross** before commissions and slippage. Pure 6-pool cheese. |
| **Order Flow / CVD / Absorption** (`CVD` divergence, delta thrusts, large-lot absorption, tape speed, spread vacuums) | `1+ billion` real `MNQ`/`NQ` ticks across `674` days | Failed to beat plain price controls out-of-sample. Worse: filtering out "disagreeing delta" entries killed our best opening trend trades (`+$529/trade` average when delta disagreed). Too much micro, zero macro. |
| **Tight Trailing Stops, Early Breakeven & `$600-$800` Daily Profit Caps** | `5,000` prop-firm Monte Carlo paths | All three increased blow-up probability. Early BE gets shaken out by normal midday noise; daily caps supply-block your account by clipping the outlier trend days needed to survive drawdowns. |
| **Paid Algo Course Audit (`81` strategy deployments)** | Real 1-tick fills + prop stops | `0` survived realistic 1-tick slippage, intraday-flat rules, and non-repainting checks. |
| **Micro Bitcoin (`MBT`) & Natural Gas (`NG`) Intraday-Flat** | `29` BTC hypotheses (`~4M` 1m bars) + `26` NG hypotheses | Too noisy and high-friction for intraday-flat prop accounts (`0` durable OOS survivors). |
| **Intraday Balance-Day Mean Reversion (`ES`/`YM`/`RTY`/`ZN`/`CL`)** | `71` variants (VWAP fades, gap fades, Value Area rotations) | High win rate (`65% to 75%`), terrible skew. One trend day wipes out weeks of small scalps. |

---

## 2. Patch Notes That Broke the Meta (2023 Daily 0DTE Regime Shift)

| Strategy | Pre-0DTE (`2019-2022`) | Post-0DTE (`2023-2026`) | Verdict |
|---|---|---|---|
| **Power-Hour (`15:00-15:55`) HOD/LOD Breakout** | `PF 2.02` (`t = +3.39`) | `-$4,391` (`PF 0.60`, `0/4` green yrs) | Complete inversion once daily 0DTE options pinned late-day swings. Meta patched. |
| **NYSE `$TICK` Zero-Line Reclaims (`EMA10` / `EMA15`)** | `+$8,648` to `+$15,204` (`4/4` green yrs) | `-$12,064` (`0/4` green yrs) | Classic breadth trigger broke completely in the 0DTE era. |

---

## 3. The Microstructure Friction Law (Why Market Selection Matters)

We measured `1 tick/side slippage + commissions` as a percentage of a normal `2 x ATR(15m)` intraday stop across 9 futures contracts (`2023 to 2026`):

| Contract | Friction % of Normal Stop | Intraday Day-Trading Reality |
|---|---:|---|
| **`MNQ` (Micro Nasdaq)** | **`1.26%`** | Rich gold mineral line. Best intraday move-to-friction ratio by far. |
| **`MGC` (Micro Gold)** | `3.15%` | Workable on clean catalysts, tighter margin for error. |
| **`MYM` (Micro Dow)** | `3.33%` | 2.6x higher friction drag than `MNQ`. |
| **`M2K` (Micro Russell)** | `3.92%` | 3.1x higher friction drag than `MNQ`. |
| **`MES` (Micro S&P 500)** | `4.23%` | 3.4x higher friction drag than `MNQ`. |
| **`MCL` (Micro Crude)** | `4.98%` | Intraday-flat exits clip 24h trend edge while paying full cost. |
| **`M6E` (Micro Euro FX)** | `17.45%` | Friction destroys almost every intraday setup. |
| **`ZN` (10-Year Treasury)** | **`20.22%`** | On `ZN` post-auction tails, **both** momentum (`-$4,243`) and its exact opposite fade (`-$3,868`) lost money to friction. |

---

## 4. Our Own Fallen Units (Killed Publicly)

Never trust a trading repo that claims a 100% research hit rate. Here's what we caught and killed ourselves:

1. **The `16.3%` Map-Hack Catch (Lookahead Leak):** Caught a subtle same-session conflict filter peeking `16.3%` of the time at whether another bot triggered later that day. Turned the map hack off immediately and killed 3 morning scalp variants that relied on it.
2. **The `VIX/VIX3M <= 0.85` Parameter Cliff:** Looked great at `0.85`, failed at `0.83` (`-$77` OOS) and `0.88` (`-$853` OOS), and failed `ES` replication (`-$982`). Demoted on the spot.
3. **The `BigGapGo` 1-Tick Mirage:** Printed `PF 2.49` DEV / `PF 1.63` OOS on 1-minute bars, then collapsed to `+$103` (`PF 1.02`) under real 1-tick replay at `10:00 ET`. Pulled from live deployment.
4. **The Silver 1-Day Mirage:** Opening breakout on Silver (`SIL`) showed `+$29,513` over 8 years, until stripping 1 freak crash day (`+$20,594`) flipped the entire 8-year edge negative (`-$1,158`). Never trust a backtest before removing the top 3 days.
