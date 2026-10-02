import { parse } from '@babel/parser'
import type { JSXAttribute, JSXElement, JSXExpressionContainer, Node } from '@babel/types'

type Element = { element: JSXElement; ancestors: JSXElement[] }
export function jsxElements(source: string): Element[] {
  const elements: Element[] = []
  function visit(value: unknown, ancestors: JSXElement[]) {
    if (!value || typeof value !== 'object') return
    const node = value as Node
    if (node.type === 'JSXElement') {
      elements.push({ element: node, ancestors })
      ancestors = [...ancestors, node]
    }
    for (const [key, part] of Object.entries(value)) {
      if (key === 'loc') continue
      if (Array.isArray(part)) part.forEach((item) => visit(item, ancestors))
      else if (part && typeof part === 'object') visit(part, ancestors)
    }
  }
  visit(parse(source, { sourceType: 'module', plugins: ['typescript', 'jsx'] }), [])
  return elements
}
function tag(element: JSXElement) {
  const name = (node: JSXElement['openingElement']['name']): string =>
    node.type === 'JSXIdentifier'
      ? node.name
      : node.type === 'JSXMemberExpression'
        ? `${name(node.object)}.${name(node.property)}`
        : ''
  return name(element.openingElement.name)
}
function attribute(element: JSXElement, name: string) {
  return element.openingElement.attributes.find(
    (item): item is JSXAttribute =>
      item.type === 'JSXAttribute' && item.name.type === 'JSXIdentifier' && item.name.name === name,
  )
}
function literal(element: JSXElement, name: string) {
  const value = attribute(element, name)?.value
  if (value?.type === 'StringLiteral') return value.value
  if (value?.type === 'JSXExpressionContainer') {
    if (value.expression.type === 'StringLiteral') return value.expression.value
    if (value.expression.type === 'BooleanLiteral') return String(value.expression.value)
    if (value.expression.type === 'NumericLiteral') return String(value.expression.value)
  }
  return null
}
function populated(value: JSXAttribute | undefined) {
  if (!value?.value) return false
  if (value.value.type === 'StringLiteral') return !!value.value.value.trim()
  if (value.value.type !== 'JSXExpressionContainer') return true
  const expression = value.value.expression
  if (expression.type === 'StringLiteral') return !!expression.value.trim()
  return (
    !['JSXEmptyExpression', 'NullLiteral', 'BooleanLiteral'].includes(expression.type) &&
    !(expression.type === 'Identifier' && expression.name === 'undefined')
  )
}
const namedTags = new Set([
  'a',
  'area',
  'article',
  'aside',
  'button',
  'dialog',
  'fieldset',
  'figure',
  'form',
  'h1',
  'h2',
  'h3',
  'h4',
  'h5',
  'h6',
  'iframe',
  'img',
  'input',
  'main',
  'menu',
  'nav',
  'ol',
  'optgroup',
  'option',
  'output',
  'progress',
  'section',
  'select',
  'summary',
  'svg',
  'table',
  'td',
  'textarea',
  'th',
  'time',
  'ul',
])
const namedRoles = new Set([
  'alert',
  'alertdialog',
  'application',
  'article',
  'banner',
  'blockquote',
  'button',
  'cell',
  'checkbox',
  'columnheader',
  'combobox',
  'complementary',
  'contentinfo',
  'definition',
  'dialog',
  'directory',
  'document',
  'feed',
  'figure',
  'form',
  'grid',
  'gridcell',
  'group',
  'heading',
  'img',
  'link',
  'list',
  'listbox',
  'listitem',
  'log',
  'main',
  'marquee',
  'math',
  'menu',
  'menubar',
  'menuitem',
  'menuitemcheckbox',
  'menuitemradio',
  'meter',
  'navigation',
  'note',
  'option',
  'progressbar',
  'radio',
  'radiogroup',
  'region',
  'row',
  'rowgroup',
  'rowheader',
  'scrollbar',
  'search',
  'searchbox',
  'separator',
  'slider',
  'spinbutton',
  'status',
  'switch',
  'tab',
  'table',
  'tablist',
  'tabpanel',
  'term',
  'textbox',
  'timer',
  'toolbar',
  'tooltip',
  'tree',
  'treegrid',
  'treeitem',
])
// Reviewed wrappers forward names onto these native hosts; new wrappers require a review.
export const namedWrappers = {
  'Dialog.Close': '@radix-ui/react-dialog Close → button',
  'Dialog.Content': '@radix-ui/react-dialog Content → dialog',
  MergeActionButton: 'features/parity-merge-overlay.tsx → button {...props}',
  RiftCover: 'themes/rift/Cover.tsx → button with explicit aria-label forwarding',
}
export function nameHostFailures(source: string) {
  return jsxElements(source).flatMap(({ element }) => {
    const name = tag(element)
    if (!(attribute(element, 'aria-label') || attribute(element, 'aria-labelledby'))) return []
    if (!/^[a-z]/.test(name))
      return name in namedWrappers ? [] : [`${element.loc?.start.line}: unreviewed named wrapper <${name}>`]
    const role = literal(element, 'role')
    if ((role && namedRoles.has(role)) || (!attribute(element, 'role') && namedTags.has(name))) return []
    // Conditional roles must provide a name-capable role on every branch.
    const expression = attribute(element, 'role')?.value
    if (
      expression?.type === 'JSXExpressionContainer' &&
      expression.expression.type === 'ConditionalExpression'
    ) {
      const { consequent, alternate } = expression.expression
      if (
        [consequent, alternate].every((value) =>
          value.type === 'StringLiteral'
            ? namedRoles.has(value.value)
            : value.type === 'Identifier' && value.name === 'undefined' && namedTags.has(name),
        )
      )
        return []
    }
    return [`${element.loc?.start.line}: <${name}> has a name but no name-capable semantic role`]
  })
}
function textContent(element: JSXElement): boolean {
  return element.children.some((child) => {
    if (child.type === 'JSXText') return !!child.value.trim()
    if (child.type === 'JSXExpressionContainer') {
      const value = child.expression
      if (value.type === 'StringLiteral') return !!value.value.trim()
      return (
        !['JSXEmptyExpression', 'NullLiteral', 'BooleanLiteral'].includes(value.type) &&
        !(value.type === 'Identifier' && value.name === 'undefined')
      )
    }
    if (child.type === 'JSXElement') return literal(child, 'aria-hidden') !== 'true' && textContent(child)
    return false
  })
}
export function interactiveNameFailures(source: string) {
  const elements = jsxElements(source)
  const reference = (element: JSXElement, name: string) => {
    const value = attribute(element, name)?.value
    return value?.type === 'JSXExpressionContainer' && value.expression.type === 'Identifier'
      ? `{${value.expression.name}}`
      : literal(element, name)
  }
  const labelIds = new Set(
    elements
      .filter(({ element }) => tag(element) === 'label' && textContent(element))
      .map(({ element }) => reference(element, 'htmlFor'))
      .filter(Boolean),
  )
  return elements.flatMap(({ element, ancestors }) => {
    const name = tag(element),
      role = literal(element, 'role')
    if (!/^[a-z]/.test(name)) return []
    const interactive =
      ['button', 'input', 'select', 'textarea', 'summary'].includes(name) ||
      (name === 'a' && !!attribute(element, 'href')) ||
      ['button', 'checkbox', 'radio', 'switch', 'slider', 'listbox', 'combobox', 'menuitem', 'tab'].includes(
        role ?? '',
      ) ||
      !!attribute(element, 'tabIndex')
    if (!interactive || literal(element, 'type') === 'hidden') return []
    if (
      populated(attribute(element, 'aria-label')) ||
      populated(attribute(element, 'aria-labelledby')) ||
      populated(attribute(element, 'title'))
    )
      return []
    if (
      ['input', 'select', 'textarea'].includes(name) &&
      (ancestors.some((parent) => tag(parent) === 'label' && textContent(parent)) ||
        labelIds.has(reference(element, 'id')))
    )
      return []
    if (!['input', 'textarea', 'select'].includes(name) && textContent(element)) return []
    return [`${element.loc?.start.line}: <${name}> has no authored name, label or text`]
  })
}
export function localMotionFailures(source: string) {
  return jsxElements(source).flatMap(({ element }) => {
    const value = attribute(element, 'style')?.value as JSXExpressionContainer | undefined
    if (value?.type !== 'JSXExpressionContainer' || value.expression.type !== 'ObjectExpression') return []
    return value.expression.properties.flatMap((property) => {
      if (property.type !== 'ObjectProperty') return []
      const name =
        property.key.type === 'Identifier'
          ? property.key.name
          : property.key.type === 'StringLiteral'
            ? property.key.value
            : ''
      return /^(transition|animation)/.test(name)
        ? [`${property.loc?.start.line}: local ${name} bypasses the shared motion styles`]
        : []
    })
  })
}

// Count scheduling sites, not imported names or type annotations. Any new site needs
// an inventory review even when it is added to an already reviewed module.
export function schedulingSites(source: string) {
  const sites: string[] = []
  function visit(value: unknown) {
    if (!value || typeof value !== 'object') return
    const node = value as Node
    if (node.type === 'CallExpression') {
      const callee = node.callee
      const name =
        callee.type === 'Identifier'
          ? callee.name
          : callee.type === 'MemberExpression' && !callee.computed && callee.property.type === 'Identifier'
            ? callee.property.name
            : ''
      if (['requestAnimationFrame', 'setTimeout', 'setInterval', 'animate'].includes(name)) sites.push(name)
    }
    if (node.type === 'JSXElement' && tag(node).startsWith('motion.')) sites.push(tag(node))
    for (const [key, part] of Object.entries(value)) {
      if (key === 'loc') continue
      if (Array.isArray(part)) part.forEach(visit)
      else if (part && typeof part === 'object') visit(part)
    }
  }
  visit(parse(source, { sourceType: 'module', plugins: ['typescript', 'jsx'] }))
  return sites.sort()
}
