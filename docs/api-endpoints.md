# İETT API endpoints — tested reference

Tested live on 2026-10-14 (~11:30 Istanbul time) with `node tools/probe/probe.mjs 500T 113334`.
Raw samples (first 5 rows of each response) are in [api-samples/](api-samples/). Re-run the probe any time to refresh them.

## Calling the İBB SOAP services

All İBB endpoints are SOAP 1.1, no auth, base `https://api.ibb.gov.tr/iett`.

```http
POST /iett/FiloDurum/SeferGerceklesme.asmx
Content-Type: text/xml; charset=utf-8
SOAPAction: "http://tempuri.org/GetHatOtoKonum_json"

<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <GetHatOtoKonum_json xmlns="http://tempuri.org/"><HatKodu>500T</HatKodu></GetHatOtoKonum_json>
  </soap:Body>
</soap:Envelope>
```

The answer is always `<{Op}Response><{Op}Result>…</{Op}Result></{Op}Response>`. What's inside `Result` depends on the method:

| Result kind | Methods | How to read it |
|---|---|---|
| **JSON string** (XML-escaped) | all `*_json` methods | XML-unescape, then `JSON.parse` |
| **XML DataSet** | `DurakDetay_GYY`, `DurakDetay_GYY_wYonAdi`, `HatServisi_GYY` | read each `<NewDataSet><Table>…</Table>` as a row; every value is text |
| **SOAP fault** (HTTP 500) | errors | `<faultstring>` holds the message |

No CORS: an `OPTIONS` preflight gets HTTP 500, so a browser can't call these directly. They have to be called from our server.

## İBB endpoints we'll use

### `GetHatOtoKonum_json(HatKodu)` — live buses on one line ⭐
`FiloDurum/SeferGerceklesme.asmx` · ~0.2–0.5 s · ~7 KB for 30 buses

| Field | Type in response | Meaning / parse as |
|---|---|---|
| `kapino` | string | Door number, the bus ID (`C-477`). Joins with `KapiNo` in the fleet feed |
| `boylam` | string (decimal) | Longitude → `double` (InvariantCulture) |
| `enlem` | string (decimal) | Latitude → `double` |
| `hatkodu` | string | Line code (`500T`) |
| `guzergahkodu` | string | Route variant `LINE_DIR_VARIANT`, e.g. `500T_G_D0`. DIR = `G` (outbound) / `D` (return) |
| `hatad` | string | Line name. Can have trailing spaces (`"ÇEKMEKÖY/SAMANDIRA-KARTAL  "`), so trim it |
| `yon` | string | Name of the stop the bus is heading to (the final stop) |
| `son_konum_zamani` | string `yyyy-MM-dd HH:mm:ss` | Last GPS time, **Istanbul local time (+03:00), no offset in the string** |
| `yakinDurakKodu` | string (int) | Code of the **geometrically nearest** stop on the route (see findings) |

No speed or plate here. Those come from the fleet feed, joined by door number.

Errors (also see the rate limit under findings):
- Unknown line: HTTP 500, `Server was unable to process request. ---> The remote server returned an error: (400) Bad Request.`
- Empty line code: HTTP 500, `… Hat Kodu Girmeden Veri cekemezsiniz.`

### `DurakDetay_GYY(hat_kodu)` — ordered stops of a line
`ibb/ibb.asmx` · ~0.5 s · ~45 KB (500T: 66 stops on D + 64 on G) · XML DataSet

| Field | Meaning / parse as |
|---|---|
| `HATKODU` | Line code |
| `YON` | `G` / `D` |
| `SIRANO` | Order in that direction, 1-based, already sorted, no gaps |
| `DURAKKODU` | Stop code (int). Matches `yakinDurakKodu` |
| `DURAKADI` | Stop name |
| `XKOORDINATI` | **Longitude** |
| `YKOORDINATI` | **Latitude** |
| `DURAKTIPI`, `ISLETMEBOLGE`, `ISLETMEALTBOLGE`, `ILCEADI` | Stop type, operating region and sub-region, district |

`DurakDetay_GYY_wYonAdi` returns the same rows plus `YON_ADI`, the destination name of that direction (e.g. `ŞİFA SONDURAK`). That's handy for labelling directions in the UI.

### `HatServisi_GYY(hat_kodu)` / `GetHat_json(HatKodu)` — line info
- `HatServisi_GYY` (XML): `HAT_KODU`, `HAT_ADI`, `TAM_HAT_ADI`, `HAT_DURUMU` (1 = active), `BOLGE`, `SEFER_SURESI` (minutes).
- `GetHat_json` (JSON): `SHATKODU`, `SHATADI`, `TARIFE` (`2 BİLETLİ`), `HAT_UZUNLUGU` (number, km), `SEFER_SURESI` (number, minutes).

### `GetDurak_json(DurakKodu)` — one stop (empty = all stops)
`SDURAKKODU` (number), `SDURAKADI`, `KOORDINAT` (**WKT text** `POINT (lon lat)`), `ILCEADI`, `SYON` (direction label), `AKILLI` (smart stop: `YOK`/…), `FIZIKI` (`AÇIK` = open-air), `DURAK_TIPI`, `ENGELLIKULLANIM` (wheelchair access).

### `GetDuyurular_json()` — announcements, all lines
~0.35 s · 115 KB · 488 rows. `HATKODU`, `HAT`, `TIP` (`Günlük` = daily), `GUNCELLEME_SAATI` (free text, e.g. `Kayit Saati: 16:07`), `MESAJ`.
`HATKODU` is sometimes plain digits (`34`), so always treat it as a string. Useful for "your line has a detour today".

### `GetFiloAracKonum_json()` — whole fleet (use sparingly)
~1.3 s · **1.1 MB** · ~6,900 buses. `Operator`, `Garaj` (often null), `KapiNo`, `Saat` (`HH:mm:ss` only, no date), `Boylam`, `Enlem`, `Hiz` (string int, km/h), `Plaka` (can be null).
The only place to get **speed and plate**. No line code. One call per minute at most, and only if we actually need speed.

### Also available, not needed now
`GetGaraj_json` (86 garages: `ID`, `GARAJ_ADI`, `GARAJ_KODU`, `KOORDINAT` WKT), `GetKazaLokasyon_json(Tarih)`, `GetBozukSatih_*`.

### Blocked
`GetStopLines_json`, `IETTPlakaServisi_Json` and `GetKaraKutu_json` are declared in the WSDL types but aren't real operations. They answer `Policy Falsified`.
Lines serving a stop are still available from metkm's `/v1/stop/{code}`, or from GTFS.

## metkm backend (third-party, optional)

`https://otobusum.metkm.win`, add `?city=istanbul`. JSON with real numbers. CORS only allows `otobusumweb.metkm.win`.

| Endpoint | Returns | Notes |
|---|---|---|
| `/bus-locations/{line}` | `[{bus_id, lng, lat, route_code, closest_stop_code, last_update_time}]` | The same data as `GetHatOtoKonum_json`, converted to numbers, with time in UTC (`…Z`). Took 1.7 s, against 0.5 s from İBB directly |
| `/bus-info/{doorNo}` | `{operator, garage, door_no, hour, lng, lat, speed, plate}` | **Stale**: returned `hour 11:01:56` at 11:30. Don't rely on it |
| `/v1/routes/{line}` | `[{id, agency_id, line_code, name, type, desc, code, path:[{lat,lng}]}]` | **27 variants, 2 MB** for 500T. The `path` polylines are good for drawing the map |
| `/v1/route-stops/{line}?direction=G` | `[{id, code, name, lng, lat, province, smart, stop_type, disabled_can_use, physical, city}]` | 80 stops on G for 500T, against 64 from İBB, so it seems to merge variants |
| `/v1/stop/{code}` | `{stop:{…}, buses:["122H","500T",…]}` | Lines serving the stop, which İBB doesn't expose |
| `/v1/search?q=` | `{stops:[…], lines:[{id, code, name, city}]}` | |
| `/timetable/{line}?direction=G` | `{code, sunday..saturday: ["HH:mm:ss", …]}` | Scheduled departures per weekday (~200/day for 500T) |

## Findings that affect the design

1. **`yakinDurakKodu` is the nearest stop, not the last stop passed.** It matched the geometrically nearest stop for 77 of 79 buses across 5 lines (500T, 131Y, 34, 15F, 19F), and the 2 misses were off by one. A bus between stops 13 and 14 may report either one. So:
   - "Stops away" = `index(myStop) − index(nearest)` in the bus's direction is **±1 accurate**.
   - To answer **"has it passed my stop?"** reliably, either track the index over successive polls (it only goes up), or project the GPS point onto the route line and compare distance along the route.
2. **The nearest stop is always in the bus's own direction** (from `guzergahkodu`) for main-variant (`_D0`) buses. Searching the other direction's list is never needed.
3. **Variant routes aren't in `DurakDetay_GYY`.** 19F had 6 variants running (`_D6536`, `_D6813`, …), and 3 buses reported stops missing from the main stop list. Those are short-turn or alternate trips. Main-variant stops come from İBB; variant stop lists need GTFS or metkm `/v1/routes`. For a first version, flag non-`_D0` buses as "alternate route" and only count stops when their nearest stop is on our list.
4. **Some buses are far from every stop.** On 500T, 4 buses were 4.7–6.5 km from their nearest stop: going to or from the garage, or a GPS fault. Ignore anything more than ~500 m from its nearest stop.
5. **Buses at index 0 are waiting at the first stop.** Several per line sit there between trips, so treat them as "not departed".
6. **Each bus updates about once a minute.** Polling every 10 s for 5 min (50 buses on 500T, 131Y and 15F) showed that GPS timestamps advance in steps of 50–71 s (median 61 s). The feed refreshes all buses together, once a minute. **Polling one line more often than every 60 s gets nothing new.** Drop a bus when its timestamp is older than ~3 min.
7. **The fleet service is rate-limited.** `SeferGerceklesme.asmx` (both `GetHatOtoKonum_json` and `GetFiloAracKonum_json`) started returning HTTP 500 `Policy Falsified` with `<l7:policyResult status="Rate limit exceeded."/>` after about 80 calls in ~4 min from one IP. The limit is per service: `ibb.asmx` and `HatDurakGuzergah.asmx` kept working at the same time. Rules that follow:
   - Poll each watched line **at most once per 60 s**, and only while someone is actually watching it.
   - Several watched lines → spread the calls across the minute. If you watch more than ~10 lines, one `GetFiloAracKonum_json` call per minute may be cheaper in requests (1.1 MB, all buses, but no line code).
   - On `Rate limit exceeded`, back off and don't retry in a loop.
   - Stop lists are static: cache them daily (different service, separate limit).
8. **The "blocked" methods really are unsupported.** For `GetStopLines_json` and the others, the gateway detail says `Service Not Found … unsupported operation`. That's a separate error from the rate limit.
9. **Times are Istanbul local without an offset.** Parse `son_konum_zamani` as `+03:00` (Türkiye has no DST).
