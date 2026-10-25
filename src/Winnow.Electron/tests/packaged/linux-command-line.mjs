const forbiddenSwitch =
  '--(?:no-sandbox|disable-(?:sandbox|setuid-sandbox|seccomp-filter-sandbox|namespace-sandbox)|no-zygote-sandbox)'

export function inspectLinuxCommandLine(commandLine) {
  const args = commandLine.split('\0').filter(Boolean)
  const command = args[0] ?? null
  const rewritten = args.length === 1 && /\s--[a-z][a-z0-9-]*(?:=|\s|$)/i.test(command)
  if (!rewritten)
    return {
      command,
      representation: 'argv',
      type:
        args
          .slice(1)
          .find((arg) => /^--type=[a-z][a-z0-9-]*$/i.test(arg))
          ?.slice(7) ?? null,
      disabledSandboxArguments: args
        .slice(1)
        .filter((arg) => new RegExp(`^${forbiddenSwitch}(?:=.*)?$`).test(arg)),
    }

  // Chromium can replace argv with one process title. Recover only bounded switches;
  // splitting on spaces would invent arguments from executable and profile paths.
  // An exact forbidden switch span is conservatively rejected even in an ambiguous title.
  return {
    command,
    representation: 'chromium-title',
    type: command.match(/(?:^|\s)--type=([a-z][a-z0-9-]*)(?=\s|$)/i)?.[1] ?? null,
    disabledSandboxArguments: [
      ...command.matchAll(new RegExp(`(?:^|\\s)(${forbiddenSwitch}(?:=[^\\s]*)?)(?=\\s|$)`, 'g')),
    ].map((match) => match[1]),
  }
}
