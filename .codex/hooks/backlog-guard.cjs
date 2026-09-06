const path = require('node:path');
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => { input += chunk; });
process.stdin.on('end', () => {
  const event = JSON.parse(input);
  const patch = event.tool_input?.command ?? '';
  const paths = [...patch.matchAll(/^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+)\r?$/gm)]
    .map(match => match[1].trim());
  if (paths.some(file => {
    const normalized = path.resolve(event.cwd ?? process.cwd(), file).replaceAll('\\', '/');
    return /\/backlog\/.*\.md$/i.test(normalized);
  })) {
    process.stdout.write(JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        permissionDecision: 'deny',
        permissionDecisionReason: 'Use the Backlog CLI to change Backlog Markdown files. See AGENTS.md.'
      }
    }));
  }
});
