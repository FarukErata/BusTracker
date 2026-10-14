import { mkdir, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const LINE = process.argv[2] ?? "500T";
const STOP = process.argv[3] ?? "113334";
const IBB = "https://api.ibb.gov.tr/iett";
const METKM = "https://otobusum.metkm.win";
const OUT = join(dirname(fileURLToPath(import.meta.url)), "..", "..", "docs", "api-samples");
const SAMPLE_ROWS = 5;

const today = new Date().toISOString().slice(0, 10);

const soapCalls = [
  ["GetHatOtoKonum_json", "FiloDurum/SeferGerceklesme.asmx", { HatKodu: LINE }],
  ["GetHatOtoKonum_json", "FiloDurum/SeferGerceklesme.asmx", { HatKodu: "NOSUCHLINE" }, "unknown-line"],
  ["GetHatOtoKonum_json", "FiloDurum/SeferGerceklesme.asmx", { HatKodu: "" }, "empty"],
  ["GetFiloAracKonum_json", "FiloDurum/SeferGerceklesme.asmx", {}],
  ["DurakDetay_GYY", "ibb/ibb.asmx", { hat_kodu: LINE }],
  ["DurakDetay_GYY_wYonAdi", "ibb/ibb.asmx", { hat_kodu: LINE }],
  ["HatServisi_GYY", "ibb/ibb.asmx", { hat_kodu: LINE }],
  ["GetHat_json", "UlasimAnaVeri/HatDurakGuzergah.asmx", { HatKodu: LINE }],
  ["GetDurak_json", "UlasimAnaVeri/HatDurakGuzergah.asmx", { DurakKodu: STOP }],
  ["GetGaraj_json", "UlasimAnaVeri/HatDurakGuzergah.asmx", {}],
  ["GetDuyurular_json", "UlasimDinamikVeri/Duyurular.asmx", {}],
  ["GetStopLines_json", "ibb/ibb.asmx", { stopCode: STOP }],
  ["IETTPlakaServisi_Json", "ibb/ibb.asmx", { KapiNo: "C-474" }],
  ["GetKaraKutu_json", "FiloDurum/SeferGerceklesme.asmx", { tarih: today, KapiNo: "", HatKodu: LINE }],
];

const restCalls = [
  ["bus-locations", `/bus-locations/${LINE}?city=istanbul`],
  ["bus-info", null],
  ["routes", `/v1/routes/${LINE}?city=istanbul`],
  ["route-stops", `/v1/route-stops/${LINE}?direction=G&city=istanbul`],
  ["stop", `/v1/stop/${STOP}?city=istanbul`],
  ["search", `/v1/search?q=${LINE}&city=istanbul`],
  ["timetable", `/timetable/${LINE}?direction=G&city=istanbul`],
];

const STRING_KINDS = [
  [/^-?\d+$/, "string(int)"],
  [/^-?\d+[.,]\d+$/, "string(decimal)"],
  [/^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}/, "string(datetime)"],
  [/^\d{2}:\d{2}(:\d{2})?$/, "string(time)"],
];

const xmlUnescape = (s) =>
  s.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&amp;/g, "&");

const isPlainObject = (v) => v !== null && typeof v === "object" && !Array.isArray(v);
const isNested = (v) => Array.isArray(v) || isPlainObject(v);
const looksLikeDataSet = (s) => s.includes("<NewDataSet") || s.includes("<Table");

function parseDataSetTables(xml) {
  return [...xml.matchAll(/<Table[^>]*>([\s\S]*?)<\/Table>/g)].map(([, body]) =>
    Object.fromEntries([...body.matchAll(/<(\w+)>([\s\S]*?)<\/\1>/g)].map(([, k, v]) => [k, xmlUnescape(v)])));
}

function findSoapResult(op, text) {
  const m = text.match(new RegExp(`<${op}Result[^>]*>([\\s\\S]*?)</${op}Result>`)) ??
            text.match(new RegExp(`<${op}Result\\s*/>`));
  return m ? m[1] ?? "" : null;
}

function parseResultText(text) {
  if (text === "") return { kind: "empty", value: null };
  try { return { kind: "json-in-xml", value: JSON.parse(text) }; }
  catch {
    if (text.startsWith("<")) return { kind: "xml-dataset", value: parseDataSetTables(text) };
    return { kind: "string", value: text.slice(0, 300) };
  }
}

function decodeSoapResult(op, text) {
  const fault = text.match(/<faultstring>([\s\S]*?)<\/faultstring>/);
  if (fault) return { kind: "fault", value: xmlUnescape(fault[1]).slice(0, 300) };
  const inner = findSoapResult(op, text);
  if (inner === null) return { kind: "unknown", value: text.slice(0, 300) };
  if (looksLikeDataSet(inner)) return { kind: "xml-dataset", value: parseDataSetTables(inner) };
  return parseResultText(xmlUnescape(inner).trim());
}

const envelope = (op, params) =>
  `<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">` +
  `<soap:Body><${op} xmlns="http://tempuri.org/">` +
  Object.entries(params).map(([k, v]) => `<${k}>${v}</${k}>`).join("") +
  `</${op}></soap:Body></soap:Envelope>`;

async function timed(fn) {
  const t = performance.now();
  const r = await fn();
  return [r, Math.round(performance.now() - t)];
}

function describeString(s) {
  if (s === "") return "string(empty)";
  return STRING_KINDS.find(([re]) => re.test(s))?.[1] ?? "string";
}

function describeValue(v) {
  if (v === null) return "null";
  if (typeof v === "string") return describeString(v);
  if (Array.isArray(v)) return `array(${v.length ? describeValue(v[0]) : "?"})`;
  return typeof v;
}

const hasExample = (v) => v !== null && v !== "";

function collectFields(rows) {
  const fields = new Map();
  for (const row of rows.filter(isPlainObject)) {
    for (const [k, v] of Object.entries(row)) {
      const f = fields.get(k) ?? { types: new Set(), example: undefined };
      f.types.add(describeValue(v));
      if (f.example === undefined && hasExample(v)) f.example = v;
      fields.set(k, f);
    }
  }
  return fields;
}

function formatExample(ex) {
  if (Array.isArray(ex)) return `[${ex.length} items]`;
  if (ex && typeof ex === "object") return JSON.stringify(ex).slice(0, 60);
  return ex;
}

const formatField = ([k, f]) =>
  `    ${k.padEnd(22)} ${[...f.types].join(" | ").padEnd(34)} e.g. ${String(formatExample(f.example)).slice(0, 60)}`;

function inferSchema(rows) {
  return [...collectFields(rows)].map(formatField).join("\n");
}

function summarizeObject(value) {
  const nested = Object.entries(value).filter(([, v]) => isNested(v));
  const nestedSchemas = nested.map(([k, v]) => `  .${k}:\n${inferSchema(Array.isArray(v) ? v : [v])}`);
  return { count: 1, schema: [inferSchema([value]), ...nestedSchemas].join("\n"), sample: value };
}

function summarize(value) {
  if (Array.isArray(value)) return { count: value.length, schema: inferSchema(value), sample: value.slice(0, SAMPLE_ROWS) };
  if (isPlainObject(value)) return summarizeObject(value);
  return { count: value == null ? 0 : 1, schema: `    ${describeValue(value)}`, sample: value };
}

function trimArray(v) {
  const trimmed = v.slice(0, SAMPLE_ROWS).map(trimLongArrays);
  return v.length > SAMPLE_ROWS ? [...trimmed, `… ${v.length - SAMPLE_ROWS} more`] : trimmed;
}

function trimLongArrays(v) {
  if (Array.isArray(v)) return trimArray(v);
  if (isPlainObject(v)) return Object.fromEntries(Object.entries(v).map(([k, x]) => [k, trimLongArrays(x)]));
  return v;
}

async function save(name, data) {
  await writeFile(join(OUT, `${name}.json`), JSON.stringify(trimLongArrays(data), null, 2) + "\n", "utf8");
}

function report(title, status, ms, bytes, kind, s) {
  console.log(`\n=== ${title}`);
  console.log(`  HTTP ${status} · ${ms} ms · ${bytes} bytes · ${kind} · ${s.count} record(s)`);
  if (s.schema) console.log(s.schema);
}

function parseJsonOrText(text) {
  try { return { kind: "json", value: JSON.parse(text) }; }
  catch { return { kind: "text", value: text.slice(0, 300) }; }
}

const soapName = ([op, , , tag]) => `ibb.${op}${tag ? `.${tag}` : ""}`;

async function probeSoap(call) {
  const [op, path, params] = call;
  const name = soapName(call);
  const url = `${IBB}/${path}`;
  const [res, ms] = await timed(() => fetch(url, {
    method: "POST",
    headers: { "Content-Type": "text/xml; charset=utf-8", SOAPAction: `"http://tempuri.org/${op}"` },
    body: envelope(op, params),
  }));
  const text = await res.text();
  const { kind, value } = decodeSoapResult(op, text);
  const s = summarize(value);
  report(`${name}  ${JSON.stringify(params)}`, res.status, ms, text.length, kind, s);
  await save(name, { request: { url, soapAction: op, params }, status: res.status, ms, bytes: text.length, kind, count: s.count, response: value });
}

async function probeRest(name, path) {
  const [res, ms] = await timed(() => fetch(METKM + path));
  const text = await res.text();
  const { kind, value } = parseJsonOrText(text);
  const s = summarize(value);
  report(`metkm.${name}  ${path}`, res.status, ms, text.length, kind, s);
  await save(`metkm.${name}`, { request: { url: METKM + path }, status: res.status, ms, bytes: text.length, count: s.count, response: value });
  return value;
}

async function attempt(name, fn) {
  try { return await fn(); }
  catch (e) { console.log(`\n=== ${name}\n  ERROR ${e.message}`); }
}

const firstBusId = (value) => (Array.isArray(value) ? value[0]?.bus_id : undefined);

await mkdir(OUT, { recursive: true });

for (const call of soapCalls) {
  await attempt(soapName(call), () => probeSoap(call));
}

let doorNo = "C-474";
for (const [name, path] of restCalls) {
  const value = await attempt(`metkm.${name}`, () => probeRest(name, path ?? `/bus-info/${doorNo}`));
  if (name === "bus-locations") doorNo = firstBusId(value) ?? doorNo;
}

console.log(`\nSamples written to ${OUT}`);
