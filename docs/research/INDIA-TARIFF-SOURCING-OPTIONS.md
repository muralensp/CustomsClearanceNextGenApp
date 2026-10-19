# India tariff data - where it can come from (options note, Day 45 item 4)

Written 2026-10-19 from web search only. **Nothing here was checked against an official page by a person, and I could not open cbic.gov.in (certificate error in the fetch tool).** Treat every statement marked *(unverified)* as a lead to confirm, not a fact. No licence text was read in full; **no legal advice**. The Product Owner (or counsel) must confirm licensing before any data is loaded.

## 1. What "the tariff" actually is (three separate things)

| Data | Authority | What it holds |
|---|---|---|
| **Customs Tariff** (First Schedule, Customs Tariff Act 1975) | CBIC | 8-digit tariff items, descriptions, units, standard (BCD) rate. Amended by Finance Act and by customs notifications. |
| **ITC-HS** (import policy Schedule I, export policy Schedule II) | DGFT | Same 8-digit codes plus policy status (free / restricted / prohibited) and licensing notes. DGFT follows the Customs Tariff; one 2026 example: Notification 26/2026-27 (27 Jul 2026) aligned Schedule II with the Finance Act 2026. |
| **Effective duty stack** | CBIC / ICEGATE | BCD + AIDC + Social Welfare Surcharge + IGST + Compensation Cess, after exemption notifications and FTA concessions (FTA rates: ITC-MACMAP, per one third-party site). |

The rules engine (ADR-0002) needs all three eventually, with effective dates: HS code and description (Customs Tariff), controlled-goods status (ITC-HS), duty rates (notifications).

## 2. Candidate sources

| # | Source | What you get | Format / access | Licence position | Fit |
|---|---|---|---|---|---|
| A | **CBIC Customs Tariff** (cbic.gov.in) | Authoritative nomenclature, units, BCD | PDFs per chapter (a 2022 example URL pattern was found). No bulk machine-readable download, API or XML found. *(unverified: could not open the site)* | Government publication. Reuse terms not found on the page; confirm. | Authoritative, but needs PDF extraction or manual entry. |
| B | **DGFT ITC-HS** (dgft.gov.in) | Schedule I/II with policy status | PDFs (FTP chapters) and notifications. Nothing machine-readable found. | As above, confirm. | Authoritative for controlled goods; same extraction work. |
| C | **ICEGATE duty calculator** (icegate.gov.in) | Effective duty stack per 8-digit code | Web tool only. No public API found. | Scraping a government portal is a terms-of-use question, not just a technical one. **Do not scrape without written confirmation.** | Good as a *check* of our computed duties, not as a feed. |
| D | **data.gov.in / GODL-India** | Open datasets under the Government Open Data Licence (royalty-free, commercial use allowed, attribution required) | APIs / CSV where a dataset exists | Reasonably clear *where a tariff dataset is actually published there* - I did **not** find one. Check before assuming. | Only if a CBIC/DGFT dataset exists; unconfirmed. |
| E | **Commercial content** (Thomson Reuters ONESOURCE, Descartes CustomsInfo, others; Indian providers such as Cusbuzz offer duty-calculator APIs) | Maintained tariff + duty + sometimes rulings; API | Paid subscription. One site lists Descartes from US$750 per user *(unverified, secondary)*; others are quote-only. | Contractual; usually restricts redistribution and storage. | Fastest and lowest effort; recurring cost; redistribution limits matter for a platform. |
| F | **Free third-party reference sites** (e.g. EximPe, which states it is built from ICEGATE/CBIC/ITC-MACMAP) | Browsable codes and rates | Web pages | Not an authorised source; their own caveat says verify against official notifications. | Useful for cross-checking only. Never load as master data. |
| G | **WCO HS nomenclature** | 6-digit international base | Paid publication | WCO copyright | Not needed: the 8-digit Indian tariff is the source of truth. |

## 3. Update cadence (what the master must tolerate)

- **Nomenclature**: changes with the Finance Act (annual, 1 Feb budget, Act in March). Budget 2026-27 split many "other" lines into named lines, so codes are added/split, not only re-rated *(secondary sources)*.
- **Rates**: change at the budget and by interim customs notifications at any time. Some changes took effect 1-2 Feb 2026 under the Provisional Collection of Taxes Act, others 1 Apr and 1 May 2026 *(secondary sources)*. Late-2026 notifications are likely; no schedule exists.
- **ITC-HS policy status**: follows the tariff with a lag (DGFT notification came months after the Finance Act in the 2026 example).
- **HS edition**: the next WCO HS edition (HS 2028) is reported to take effect 1 Jan 2028, with 299 groups of amendments published January 2026 *(secondary source)*. India's adoption date is not confirmed. That is a one-time large re-mapping.

Implication: the master needs **effective dating on every row** (it already fits the rules-engine design) and a repeatable load process, not a one-off import.

## 4. Options for Phase 1

| Option | Description | Cost / effort | Risk |
|---|---|---|---|
| **1. Official PDFs, extracted by us** | Parse CBIC First Schedule and DGFT ITC-HS PDFs into a versioned load file; apply notifications by hand | Low cash; high and recurring effort; PDF parsing is error-prone | Wrong rates silently reach filings; needs a verification step and clear provenance |
| **2. Commercial tariff content (API or file feed)** | Subscribe to a maintained India dataset | Subscription; low engineering | Lock-in; redistribution/storage limits; still must verify |
| **3. Hybrid (recommended to evaluate)** | Official sources define the code list and units; a commercial feed or the ICEGATE calculator is used to *cross-check* rates; every row keeps `Source`, `IsVerified`, effective dates | Medium | Two sources can disagree; needs a rule for which wins |
| 4. Scrape ICEGATE / third-party sites | - | Low | **Not recommended**: terms of use unknown, no authority, fragile |

**Suggested direction (a recommendation, not a decision):** start with Option 3 for the code list plus ITC-HS status from the official PDFs, and ask two or three commercial vendors for a quote and for their redistribution terms before deciding whether rates come from them. Keep the Day 39 pattern: `Source` and `IsVerified` on every row, nothing marked verified until compared to an official notification.

## 5. Questions only the Product Owner can answer

1. Will the platform *file* declarations (ICEGATE) or only advise? Filing raises the cost of a wrong rate sharply.
2. Is a paid content subscription acceptable, and what budget?
3. Does the target customer base need FTA/preferential rates in Phase 1?
4. Who signs off that loaded data matches the official notification (the Day 39 `IsVerified` flag needs an owner)?

## 6. What I could not establish

- Whether CBIC, DGFT or ICEGATE offer any machine-readable download or API, or a data-reuse notice (site unreachable from this tool; web search found none).
- Whether data.gov.in publishes a customs tariff dataset.
- Actual vendor prices and redistribution terms for India content.
- India's adoption date for HS 2028.
- The exact current rates and the current Customs Tariff edition; none were read from an official source.
