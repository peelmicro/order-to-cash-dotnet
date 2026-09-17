import { readFileSync } from 'node:fs';
import path from 'node:path';
import ts from 'typescript';

/**
 * The response statuses `specs/shared/openapi.yaml` declares for each
 * operation, read from the GENERATED types (`src/generated/openapi.ts`, which
 * `pnpm types:check` keeps in step with the spec) with the TypeScript parser.
 * Types are erased at run time, so the declarations are read as source.
 */
export const GENERATED = path.resolve(import.meta.dirname, '..', 'generated', 'openapi.ts');

export interface DeclaredOperation {
  method: string;
  /** Gateway path template, e.g. `/orders/{orderId}`. */
  template: string;
  operationId: string;
  statuses: number[];
}

function interfaceNamed(file: ts.SourceFile, name: string): ts.InterfaceDeclaration {
  const found = file.statements.find((s): s is ts.InterfaceDeclaration => ts.isInterfaceDeclaration(s) && s.name.text === name);
  if (!found) throw new Error(`openapi.ts has no interface ${name}`);
  return found;
}

const memberName = (member: ts.TypeElement): string | undefined => (member.name && (ts.isIdentifier(member.name) || ts.isStringLiteral(member.name) || ts.isNumericLiteral(member.name)) ? member.name.text : undefined);

export function declaredOperations(file: string = GENERATED): DeclaredOperation[] {
  const source = ts.createSourceFile(file, readFileSync(file, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const statuses = new Map<string, number[]>();
  for (const op of interfaceNamed(source, 'operations').members) {
    const id = memberName(op);
    if (!id || !ts.isPropertySignature(op) || !op.type || !ts.isTypeLiteralNode(op.type)) continue;
    const responses = op.type.members.find((m) => memberName(m) === 'responses');
    if (!responses || !ts.isPropertySignature(responses) || !responses.type || !ts.isTypeLiteralNode(responses.type)) continue;
    statuses.set(
      id,
      responses.type.members
        .map(memberName)
        .filter((n): n is string => n !== undefined && /^\d{3}$/.test(n))
        .map(Number),
    );
  }
  const out: DeclaredOperation[] = [];
  for (const entry of interfaceNamed(source, 'paths').members) {
    const template = memberName(entry);
    if (!template || !ts.isPropertySignature(entry) || !entry.type || !ts.isTypeLiteralNode(entry.type)) continue;
    for (const method of entry.type.members) {
      const name = memberName(method);
      if (!name || !ts.isPropertySignature(method) || !method.type) continue;
      // `post: operations["login"]`
      const type = method.type;
      if (!ts.isIndexedAccessTypeNode(type) || !ts.isLiteralTypeNode(type.indexType) || !ts.isStringLiteral(type.indexType.literal)) continue;
      const operationId = type.indexType.literal.text;
      out.push({ method: name.toUpperCase(), template, operationId, statuses: statuses.get(operationId) ?? [] });
    }
  }
  return out;
}

/** The declared operation a Gateway request (`METHOD /path`, query ignored) is an instance of. */
export function operationFor(method: string, gatewayPath: string, operations: DeclaredOperation[] = declaredOperations()): DeclaredOperation | undefined {
  const pathOnly = gatewayPath.split('?')[0]!;
  return operations.find((op) => op.method === method.toUpperCase() && new RegExp(`^${op.template.replace(/[.*+?^$()|[\]\\]/g, '\\$&').replace(/\\?\{[^}]+\\?\}|\{[^}]+\}/g, '[^/]+')}$`).test(pathOnly));
}
