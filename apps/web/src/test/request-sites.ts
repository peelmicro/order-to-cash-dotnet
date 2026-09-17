import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

/**
 * Two STRUCTURAL premises of the behavioural error-text sweep
 * (src/app/error-text-sweep.test.tsx), read from the source with the
 * TypeScript parser (a comment or a string that merely reads like code is not
 * a node):
 *
 *  1. `requestPrimitives` — every place browser code can make a request
 *     WITHOUT going through `src/lib/api-client.ts`. The sweep observes
 *     requests at that module's `setApiFetch` seam, so a request made any other
 *     way is invisible to it unless it is listed and covered separately.
 *  2. `mutatingCalls` — every `apiRequest(…)` call whose method is not GET.
 *     A mutation happens only after a user action, so the sweep can only fail
 *     it if a scripted action makes it; this is the population those scripts
 *     are checked against.
 */
export const SRC = path.resolve(import.meta.dirname, '..');

/** By PATH: generated types, test helpers, test files, and server-only code (`src/server/**`) are not browser code. */
export function isBrowserSource(rel: string): boolean {
  if (!/\.(ts|tsx|js|jsx|mts|mjs)$/.test(rel)) return false;
  if (/\.test\.[a-z]+$/.test(rel)) return false;
  return !(rel.startsWith('generated/') || rel.startsWith('test/') || rel.startsWith('server/'));
}

export function browserSourceFiles(root: string = SRC): string[] {
  const out: string[] = [];
  const walk = (dir: string) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else {
        const rel = path.relative(root, full).split(path.sep).join('/');
        if (isBrowserSource(rel)) out.push(rel);
      }
    }
  };
  walk(root);
  return out.sort();
}

function parse(rel: string, root: string): ts.SourceFile {
  const kind = /\.[jt]sx$/.test(rel) ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  return ts.createSourceFile(rel, readFileSync(path.join(root, rel), 'utf8'), ts.ScriptTarget.Latest, true, kind);
}

const lineOf = (file: ts.SourceFile, node: ts.Node) => file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1;

/** The API client module: the seam itself, where the one sanctioned `fetch` lives. */
export const API_CLIENT = 'lib/api-client.ts';

/** Identifiers (and `x['name']` keys) that make a request in a browser. */
export const PRIMITIVE_NAMES = ['fetch', 'EventSource', 'XMLHttpRequest', 'WebSocket', 'sendBeacon', 'axios', 'ky', 'ofetch'] as const;
/** Modules whose import is itself a request client. */
export const CLIENT_MODULES = ['axios', 'ky', 'ofetch', 'node-fetch', 'cross-fetch', 'undici', 'isomorphic-fetch', 'superagent', 'got'];

export interface PrimitiveHit {
  /** `file | primitive` — what an exception is keyed by. */
  key: string;
  /** `file:line primitive` — what a failure names. */
  site: string;
}

/**
 * Every request primitive in browser code outside the API client:
 *   - an identifier named in {@link PRIMITIVE_NAMES} (a call, a `new`, a reference passed along, a property name);
 *   - an element access with such a string key (`window['fetch']`);
 *   - an import from a request-client module;
 *   - a JSX `action` / `formAction` attribute (a native form post is a request too).
 */
export function requestPrimitives(root: string = SRC): PrimitiveHit[] {
  const hits: PrimitiveHit[] = [];
  const names = new Set<string>(PRIMITIVE_NAMES);
  for (const rel of browserSourceFiles(root)) {
    if (rel === API_CLIENT) continue;
    const file = parse(rel, root);
    const add = (node: ts.Node, primitive: string) => hits.push({ key: `${rel} | ${primitive}`, site: `${rel}:${lineOf(file, node)} ${primitive}` });
    const visit = (n: ts.Node) => {
      if (ts.isIdentifier(n) && names.has(n.text)) add(n, n.text);
      else if (ts.isElementAccessExpression(n) && ts.isStringLiteralLike(n.argumentExpression) && names.has(n.argumentExpression.text)) add(n, n.argumentExpression.text);
      else if ((ts.isImportDeclaration(n) || ts.isExportDeclaration(n)) && n.moduleSpecifier && ts.isStringLiteral(n.moduleSpecifier) && CLIENT_MODULES.includes(n.moduleSpecifier.text)) add(n, `import ${n.moduleSpecifier.text}`);
      else if (ts.isCallExpression(n) && n.expression.kind === ts.SyntaxKind.ImportKeyword && n.arguments[0] && ts.isStringLiteralLike(n.arguments[0]) && CLIENT_MODULES.includes(n.arguments[0].text)) add(n, `import ${n.arguments[0].text}`);
      else if (ts.isJsxAttribute(n) && ts.isIdentifier(n.name) && (n.name.text === 'action' || n.name.text === 'formAction')) add(n, 'form action');
      ts.forEachChild(n, visit);
    };
    visit(file);
  }
  return hits;
}

export interface MutatingCall {
  /** `file:line` */
  site: string;
  method: string;
  /** The path with every interpolation shown as `{}`, e.g. `/api/invoices/{}/payments`; `undefined` when it cannot be read. */
  pattern: string | undefined;
  /** Why the call cannot be classified, if it cannot. A call that cannot be classified is a failure, never a skip. */
  unresolved?: string;
}

function pathPattern(node: ts.Node | undefined): string | undefined {
  if (!node) return undefined;
  if (ts.isStringLiteralLike(node)) return node.text;
  if (ts.isTemplateExpression(node)) return node.head.text + node.templateSpans.map((span) => `{}${span.literal.text}`).join('');
  return undefined;
}

/** A request pattern as a matcher for an observed path (`{}` = one or more characters other than `/` and `?`). */
export function patternMatches(pattern: string, pathname: string): boolean {
  const source = pattern
    .split('{}')
    .map((part) => part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('[^/?]+');
  return new RegExp(`^${source}$`).test(pathname);
}

/**
 * Every `apiRequest(…)` call whose method is not GET, in browser code
 * (including the API client itself). `apiRequest` is recognised however it is
 * bound: its declaration in the client, a named import (renamed or not), or a
 * namespace import. Any OTHER use of the name — passing it along, aliasing it
 * — is reported as unresolved, because a request made through an alias is one
 * this scan cannot classify.
 */
export function mutatingCalls(root: string = SRC): MutatingCall[] {
  const out: MutatingCall[] = [];
  for (const rel of browserSourceFiles(root)) {
    const file = parse(rel, root);
    const direct = new Set<string>();
    const namespaces = new Set<string>();
    if (rel === API_CLIENT) direct.add('apiRequest');
    for (const statement of file.statements) {
      if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier)) continue;
      const from = statement.moduleSpecifier.text;
      if (from !== '@/lib/api-client' && !from.endsWith('/api-client') && !from.endsWith('/lib/api-client')) continue;
      const bindings = statement.importClause?.namedBindings;
      if (bindings && ts.isNamespaceImport(bindings)) namespaces.add(bindings.name.text);
      if (bindings && ts.isNamedImports(bindings)) {
        for (const element of bindings.elements) if ((element.propertyName ?? element.name).text === 'apiRequest') direct.add(element.name.text);
      }
    }
    if (direct.size === 0 && namespaces.size === 0) continue;

    const isCallee = (n: ts.Node) => ts.isCallExpression(n.parent) && n.parent.expression === n;
    const refersToApiRequest = (n: ts.Node): boolean =>
      (ts.isIdentifier(n) && direct.has(n.text)) || (ts.isPropertyAccessExpression(n) && ts.isIdentifier(n.expression) && namespaces.has(n.expression.text) && n.name.text === 'apiRequest');

    const visit = (n: ts.Node) => {
      const site = `${rel}:${lineOf(file, n)}`;
      if (refersToApiRequest(n) && !ts.isImportSpecifier(n.parent) && !ts.isFunctionDeclaration(n.parent)) {
        if (!isCallee(n)) {
          // `apiRequest` in a namespace access is visited twice (the access and its name); report the access only.
          if (!(ts.isIdentifier(n) && ts.isPropertyAccessExpression(n.parent) && n.parent.name === n)) out.push({ site, method: '?', pattern: undefined, unresolved: 'apiRequest is referenced other than by a direct call (aliased or passed along)' });
        } else {
          const call = n.parent as ts.CallExpression;
          const pattern = pathPattern(call.arguments[0]);
          const init = call.arguments[1];
          let method: string | undefined;
          let unresolved: string | undefined;
          if (!init) method = 'GET';
          else if (!ts.isObjectLiteralExpression(init)) unresolved = 'the request options are not an object literal';
          else {
            method = 'GET';
            for (const property of init.properties) {
              if (ts.isSpreadAssignment(property)) {
                method = undefined;
                unresolved = 'the request options spread another object, so the method cannot be read';
              } else if (property.name && ts.isIdentifier(property.name) && property.name.text === 'method') {
                if (ts.isPropertyAssignment(property) && ts.isStringLiteralLike(property.initializer)) {
                  method = property.initializer.text.toUpperCase();
                  unresolved = undefined;
                } else {
                  method = undefined;
                  unresolved = 'the method is not a string literal';
                }
              }
            }
          }
          if (method === 'GET' && !unresolved) {
            // A read: the page-load sweep observes it.
          } else if (unresolved || method === undefined) {
            out.push({ site, method: method ?? '?', pattern, unresolved });
          } else if (pattern === undefined) {
            out.push({ site, method, pattern, unresolved: 'the path is not a string or template literal' });
          } else {
            out.push({ site, method, pattern });
          }
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(file);
  }
  return out;
}

// ── Who can trigger a request after load ───────────────────────────────────

export type UnitKind = 'mutation' | 'gated-query' | 'query' | 'value';

export interface Unit {
  /** `hooks/use-x.ts#useThing` for a hook export; `features/x.tsx#*` for a component file that makes a mutation or a gated read itself. */
  key: string;
  kind: UnitKind;
  /** Browser files that import it (`#*`: that import anything from the file), sorted. */
  consumers: string[];
  /** For a mutation or gated read: the files that import those consumers (normally the pages), sorted — a second page rendering the same component is a second way in. */
  via: string[];
}

const isCallTo = (n: ts.Node, name: string): n is ts.CallExpression => ts.isCallExpression(n) && ts.isIdentifier(n.expression) && n.expression.text === name;

/** `mutation` if the node contains `useMutation(…)`; `gated-query` if it contains `useQuery({ … enabled … })`; `query` if it contains any other `useQuery(…)`. */
function kindOf(node: ts.Node): UnitKind {
  let kind: UnitKind = 'value';
  const rank: Record<UnitKind, number> = { value: 0, query: 1, 'gated-query': 2, mutation: 3 };
  const raise = (next: UnitKind) => {
    if (rank[next] > rank[kind]) kind = next;
  };
  const visit = (n: ts.Node) => {
    if (isCallTo(n, 'useMutation')) raise('mutation');
    if (isCallTo(n, 'useQuery')) {
      const options = n.arguments[0];
      const gated = options !== undefined && ts.isObjectLiteralExpression(options) && options.properties.some((p) => p.name !== undefined && ts.isIdentifier(p.name) && p.name.text === 'enabled');
      raise(gated ? 'gated-query' : 'query');
    }
    ts.forEachChild(n, visit);
  };
  visit(node);
  return kind;
}

/** The src-relative file an import specifier names (`@/hooks/use-stock` → `hooks/use-stock`), without extension; `undefined` for a package. */
function resolveSpecifier(fromRel: string, specifier: string): string | undefined {
  if (specifier.startsWith('@/')) return specifier.slice(2);
  if (specifier.startsWith('.')) return path.posix.normalize(path.posix.join(path.posix.dirname(fromRel), specifier));
  return undefined;
}

const stripExtension = (rel: string) => rel.replace(/\.[jt]sx?$/, '');

/**
 * Every export of `src/hooks/*` (functions and consts; types carry no
 * behaviour), and every non-hook browser file that itself calls `useMutation`,
 * a gated `useQuery`, or a mutating `apiRequest`, with the files that import it.
 * Derived by content, so a SECOND consumer of a shared mutation — or a new
 * component that mounts a read behind a click — shows up as a changed line.
 */
export function requestUnits(root: string = SRC): Unit[] {
  const files = browserSourceFiles(root);
  const parsed = new Map(files.map((rel) => [rel, parse(rel, root)] as const));
  const mutatingFiles = new Set(mutatingCalls(root).map((call) => call.site.split(':')[0]!));

  // module (without extension) → importer → imported names ('*' for a namespace import)
  const importers = new Map<string, Map<string, Set<string>>>();
  for (const [rel, file] of parsed) {
    for (const statement of file.statements) {
      if (!ts.isImportDeclaration(statement) || !ts.isStringLiteral(statement.moduleSpecifier) || statement.importClause?.isTypeOnly) continue;
      const target = resolveSpecifier(rel, statement.moduleSpecifier.text);
      if (!target) continue;
      const names = new Set<string>();
      const clause = statement.importClause;
      if (clause?.name) names.add('default');
      const bindings = clause?.namedBindings;
      if (bindings && ts.isNamespaceImport(bindings)) names.add('*');
      if (bindings && ts.isNamedImports(bindings)) for (const element of bindings.elements) if (!element.isTypeOnly) names.add((element.propertyName ?? element.name).text);
      const byImporter = importers.get(target) ?? new Map<string, Set<string>>();
      byImporter.set(rel, new Set([...(byImporter.get(rel) ?? []), ...names]));
      importers.set(target, byImporter);
    }
  }
  const consumersOf = (rel: string, name: string | undefined) =>
    [...(importers.get(stripExtension(rel)) ?? new Map<string, Set<string>>())]
      .filter(([, names]) => name === undefined || names.has(name) || names.has('*'))
      .map(([importer]) => importer)
      .sort();

  const withVia = (unit: Unit): Unit =>
    unit.kind === 'mutation' || unit.kind === 'gated-query' ? { ...unit, via: [...new Set(unit.consumers.flatMap((consumer) => consumersOf(consumer, undefined)))].sort() } : unit;

  const units: Unit[] = [];
  for (const [rel, file] of parsed) {
    if (rel.startsWith('hooks/')) {
      for (const statement of file.statements) {
        const exported = ts.canHaveModifiers(statement) && ts.getModifiers(statement)?.some((m) => m.kind === ts.SyntaxKind.ExportKeyword);
        if (!exported) continue;
        const names: string[] = [];
        if (ts.isFunctionDeclaration(statement) && statement.name) names.push(statement.name.text);
        if (ts.isVariableStatement(statement)) for (const d of statement.declarationList.declarations) if (ts.isIdentifier(d.name)) names.push(d.name.text);
        for (const name of names) units.push(withVia({ key: `${rel}#${name}`, kind: kindOf(statement), consumers: consumersOf(rel, name), via: [] }));
      }
    } else {
      const kind = kindOf(file);
      const own = mutatingFiles.has(rel) ? 'mutation' : kind;
      if (own === 'mutation' || own === 'gated-query') units.push(withVia({ key: `${rel}#*`, kind: own, consumers: consumersOf(rel, undefined), via: [] }));
    }
  }
  return units.sort((a, b) => a.key.localeCompare(b.key));
}
