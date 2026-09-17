import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

/**
 * Where component and route tests serve a REAL captured Gateway response
 * (src/test/fixtures/gateway/), and for which route. Kept from id 29's fix
 * round 1 when that round's syntax-based error-site guard was replaced by the
 * behavioural page sweep (src/app/error-text-sweep.test.tsx): the sweep proves
 * each page shows a failure's own words using synthetic sentinels, and says
 * nothing about whether the tests that use REAL captures serve each one on the
 * route it was captured from — which is what this checks (fix round 1, arms A7/A9).
 */
const SRC = path.resolve(import.meta.dirname, '..');

/** Same-file `const name = <initialiser>` texts, enough to resolve a computed route key such as `[DETAIL_PATH]`. */
function declarations(file: ts.SourceFile): Map<string, string[]> {
  const out = new Map<string, string[]>();
  const visit = (n: ts.Node) => {
    if (ts.isVariableDeclaration(n) && ts.isIdentifier(n.name) && n.initializer) out.set(n.name.text, [...(out.get(n.name.text) ?? []), n.initializer.getText(file)]);
    ts.forEachChild(n, visit);
  };
  visit(file);
  return out;
}

function parse(rel: string, root: string): ts.SourceFile {
  return ts.createSourceFile(rel, readFileSync(path.join(root, rel), 'utf8'), ts.ScriptTarget.Latest, true, rel.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
}

function literalText(node: ts.Node | undefined): string | undefined {
  if (node && (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))) return node.text;
  return undefined;
}

export function testFiles(root: string = SRC): string[] {
  const out: string[] = [];
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (/\.test\.(ts|tsx)$/.test(entry.name)) out.push(path.relative(root, full).split(path.sep).join('/'));
    }
  };
  walk(root);
  return out.sort();
}

export interface FixtureUse {
  /** `file | METHOD /gateway/path | fixture` — the route a fixture is served for, as the Gateway would see it. */
  key: string;
  file: string;
  route: string;
  fixture: string;
}

function templateText(node: ts.Node, decls: Map<string, string[]>): string | undefined {
  const direct = literalText(node);
  if (direct !== undefined) return direct;
  if (ts.isTemplateExpression(node)) return node.head.text + node.templateSpans.map((span) => `*${span.literal.text}`).join('');
  if (ts.isIdentifier(node)) {
    const init = decls.get(node.text)?.[0];
    if (init === undefined) return undefined;
    return init.replace(/^[`'"]|[`'"]$/g, '').replace(/\$\{[^}]*\}/g, '*');
  }
  return undefined;
}

/** Literal fixture names served by `fixtureResponse(…)`, `gatewayFixture(…)` or a `relayFixture(…)` helper inside `node`. */
function servedFixtures(node: ts.Node): string[] {
  const names: string[] = [];
  const walk = (m: ts.Node) => {
    if (ts.isCallExpression(m) && ts.isIdentifier(m.expression) && ['fixtureResponse', 'gatewayFixture', 'relayFixture'].includes(m.expression.text)) {
      const arg = literalText(m.arguments[0]);
      if (arg) names.push(arg);
    }
    ts.forEachChild(m, walk);
  };
  walk(node);
  return names;
}

/**
 * Every place a test serves a captured fixture for a route it NAMES LITERALLY:
 *   - an object-literal route table entry `'GET /api/x': () => fixtureResponse('f')` (key: the browser route, `/api` dropped);
 *   - `gateway.on('GET', '/x', … gatewayFixture('f') …)` (key: the Gateway route).
 * Non-literal pairings (an `it.each` table, a raw `createServer`) are classified by hand in the record.
 */
export function fixtureUses(root: string = SRC): FixtureUse[] {
  const uses: FixtureUse[] = [];
  for (const rel of testFiles(root)) {
    const file = parse(rel, root);
    const decls = declarations(file);
    const visit = (n: ts.Node) => {
      if (ts.isPropertyAssignment(n)) {
        const keyNode = ts.isComputedPropertyName(n.name) ? n.name.expression : n.name;
        const key = templateText(keyNode, decls);
        const match = key?.match(/^(GET|POST|PUT|PATCH|DELETE) \/api(\/\S*)$/);
        if (match) for (const fixture of servedFixtures(n.initializer)) uses.push({ key: `${rel} | ${match[1]} ${match[2]} | ${fixture}`, file: rel, route: `${match[1]} ${match[2]}`, fixture });
      }
      if (ts.isCallExpression(n) && ts.isPropertyAccessExpression(n.expression) && n.expression.name.text === 'on' && n.arguments.length === 3) {
        const method = literalText(n.arguments[0]);
        const route = literalText(n.arguments[1]);
        if (method && route) for (const fixture of servedFixtures(n.arguments[2]!)) uses.push({ key: `${rel} | ${method} ${route} | ${fixture}`, file: rel, route: `${method} ${route}`, fixture });
      }
      ts.forEachChild(n, visit);
    };
    visit(file);
  }
  return uses.sort((a, b) => a.key.localeCompare(b.key));
}

/** `GET /orders/{random uuid}` matches `GET /orders/*`; a query string is not part of the route. */
export function sameRoute(capturedFrom: string, route: string): boolean {
  const split = (r: string) => {
    // Split on the FIRST space only: a captured placeholder may contain one (`{issued invoice}`).
    const at = r.indexOf(' ');
    return { method: r.slice(0, at), segments: r.slice(at + 1).split('?')[0]!.split('/') };
  };
  const a = split(capturedFrom);
  const b = split(route);
  if (a.method !== b.method || a.segments.length !== b.segments.length) return false;
  const wild = (s: string) => s === '*' || /^\{.*\}$/.test(s);
  return a.segments.every((s, i) => s === b.segments[i] || wild(s) || wild(b.segments[i]!));
}
