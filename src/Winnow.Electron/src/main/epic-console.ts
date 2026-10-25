import { spawn } from 'node:child_process'
import { activationHostCommand, type ActivationHostLocation } from './activation-host'

export const epicConsoleRequested = (args: readonly string[]) => args.includes('--epic-login')

export function epicConsoleArguments(
  args: readonly string[],
  owner: number,
  terminalParent: number,
): string[] {
  const result = ['--epic-login']
  for (let index = 0; index < args.length; index++) {
    const arg = args[index]
    if (arg === '--code' || arg === '--data-dir') {
      result.push(arg)
      if (index + 1 < args.length && !args[index + 1].startsWith('--')) result.push(args[++index])
    } else if (arg.startsWith('--code=') || arg.startsWith('--data-dir=') || arg === '--no-sync') {
      result.push(arg)
    }
  }
  result.push('--console-owner-pid', String(owner), '--terminal-parent-pid', String(terminalParent))
  return result
}

/** Delegate before GUI/profile/activation startup. The companion owns terminal handles and the API attempt. */
export async function runEpicConsole(
  args: readonly string[],
  location: ActivationHostLocation,
  launch: typeof spawn = spawn,
): Promise<number> {
  const environment = location.environment ?? process.env
  const host = await activationHostCommand({
    ...location,
    environment: { ...environment, WINNOW_ACTIVATION_HELPER_PATH: environment.WINNOW_BACKEND_PATH },
  })
  return new Promise<number>((resolve, reject) => {
    const child = launch(
      host.command,
      [...host.prefix, ...epicConsoleArguments(args, process.pid, process.ppid)],
      { cwd: host.cwd, windowsHide: true, stdio: 'inherit', env: environment },
    )
    child.once('error', reject)
    // No captured stdout and no protocol reader: redirected stdin and terminal interaction belong
    // to the companion. On owner death it cancels its attempt using its own bounded cleanup token.
    child.once('exit', (code) => resolve(code ?? 1))
  })
}
