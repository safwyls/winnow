import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { openExternal, request } from '../api/client'
import type { BackendOperation, Mode, PluginSetting, PluginSnapshot } from '../api/types'
import { useApiQuery } from '../api/hooks'
import { useViewState } from '../viewState'
import { BackendRestart } from './BackendRestart'
import { OfficialPluginInstall } from './SettingsPreferences'
import { PluginAccount } from './PluginAccount'
import { pluginInstallationNote, pluginSecretNote, pluginWebUrl } from './plugin-policy'
import './plugin-settings.css'

const manage = '@manage'
export function PluginSettings({ mode }: { mode: Mode }) {
  const operations = useApiQuery<BackendOperation[]>('operations.get')
  const plugins = useQuery({
    queryKey: ['api', 'plugins.get', undefined],
    queryFn: ({ signal }) => request<PluginSnapshot[]>('plugins.get', undefined, undefined, signal),
    retry: false,
    staleTime: 30_000,
    refetchOnMount: 'always',
  })
  const [saved, setSaved] = useViewState<string | null>(`${mode}:plugins:selected`, null)
  const loaded = [...(plugins.data ?? [])]
    .filter((plugin) => plugin.isLoaded)
    .sort((left, right) => left.name.localeCompare(right.name))
  const selected =
    saved === null ? (loaded[0]?.id ?? manage) : loaded.some((plugin) => plugin.id === saved) ? saved : manage
  useEffect(() => {
    if (plugins.data && saved !== selected) setSaved(selected)
  }, [plugins.data, saved, selected, setSaved])
  const [folderError, setFolderError] = useState('')
  const [installing, setInstalling] = useState(false)
  const writing = useIsMutating({ mutationKey: ['plugin-write'] }) > 0
  const activeInstallation = operations.data?.some(
    (operation) =>
      operation.kind === 'plugin-install' && ['queued', 'running'].includes(operation.state.toLowerCase()),
  )
  const panelId = useId()
  return (
    <section className="plugin-settings" aria-label="Plugins">
      <PluginTabs
        mode={mode}
        selected={selected}
        select={setSaved}
        panelId={panelId}
        tabs={[
          ...loaded.map((plugin) => ({ id: plugin.id, name: plugin.name })),
          { id: manage, name: 'Manage plugins' },
        ]}
      />
      {plugins.error && (
        <p role="alert">
          Could not read plugins. Choose Restart library service in Manage plugins to try again.
        </p>
      )}
      {plugins.isPending && <p role="status">Reading plugins…</p>}
      <div role="tabpanel" id={panelId} aria-labelledby={`${panelId}-${selected}`}>
        {selected === manage ? (
          <>
            <section className="feature-panel plugin-management">
              <h2>Manage plugins</h2>
              <p>{pluginInstallationNote}</p>
              <button
                disabled={!window.winnow.openDataFolder}
                onClick={() => {
                  setFolderError('')
                  void window.winnow
                    .openDataFolder?.('plugins')
                    .catch(() =>
                      setFolderError(
                        "Could not open the plugins folder. Check that Winnow's data folder is available, then try again.",
                      ),
                    )
                }}
              >
                Open plugins folder
              </button>
              {folderError && <p role="alert">{folderError}</p>}
              <h3>Loaded in this session</h3>
              {loaded.length ? (
                <ul>
                  {loaded.map((plugin) => (
                    <li key={plugin.id}>
                      {plugin.name} · {plugin.version}
                    </li>
                  ))}
                </ul>
              ) : (
                <p>No plugins are loaded in this session.</p>
              )}
            </section>
            <BackendRestart disabled={writing || installing || activeInstallation} />
            <OfficialPluginInstall showManualHelp={false} onBusyChange={setInstalling} />
            {(plugins.data ?? [])
              .filter((plugin) => !plugin.isLoaded)
              .map((plugin) => (
                <PluginCard key={`${mode}:${plugin.id}`} plugin={plugin} mode={mode} />
              ))}
          </>
        ) : (
          loaded
            .filter((plugin) => plugin.id === selected)
            .map((plugin) => <PluginCard key={`${mode}:${plugin.id}`} plugin={plugin} mode={mode} />)
        )}
      </div>
    </section>
  )
}

function PluginTabs({
  tabs,
  selected,
  select,
  mode,
  panelId,
}: {
  tabs: { id: string; name: string }[]
  selected: string
  select(id: string): void
  mode: Mode
  panelId: string
}) {
  const strip = useRef<HTMLDivElement>(null)
  const [edges, setEdges] = useState({ overflow: false, start: true, end: true })
  function measure() {
    const node = strip.current
    if (node)
      setEdges({
        overflow: node.scrollWidth > node.clientWidth + 1,
        start: node.scrollLeft <= 1,
        end: node.scrollLeft + node.clientWidth >= node.scrollWidth - 1,
      })
  }
  useLayoutEffect(() => {
    measure()
    const observer =
      typeof ResizeObserver !== 'undefined'
        ? new ResizeObserver(() => {
            strip.current
              ?.querySelector<HTMLElement>('[aria-selected="true"]')
              ?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
            measure()
          })
        : null
    if (strip.current) observer?.observe(strip.current)
    return () => observer?.disconnect()
  }, [tabs.map((tab) => tab.id + tab.name).join('|')])
  useLayoutEffect(() => {
    strip.current
      ?.querySelector<HTMLElement>('[aria-selected="true"]')
      ?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
  }, [selected])
  return (
    <div className="plugin-tab-bar">
      {edges.overflow && (
        <button
          aria-label="Scroll plugin tabs left"
          disabled={edges.start}
          onClick={() => strip.current?.scrollBy({ left: -strip.current.clientWidth * 0.7 })}
        >
          <ChevronLeft aria-hidden="true" />
        </button>
      )}
      <div
        className="tabs plugin-tabs"
        role="tablist"
        aria-label="Plugin settings"
        ref={strip}
        onScroll={measure}
      >
        {tabs.map((tab, index) => (
          <button
            key={tab.id}
            role="tab"
            id={`${panelId}-${tab.id}`}
            aria-selected={selected === tab.id}
            aria-controls={panelId}
            tabIndex={mode === 'fullscreen' || selected === tab.id ? 0 : -1}
            onClick={() => select(tab.id)}
            onFocus={(event) => event.currentTarget.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })}
            onKeyDown={(event) => {
              if (mode === 'fullscreen' && event.key === 'ArrowDown') {
                const destination = document
                  .getElementById(panelId)
                  ?.querySelector<HTMLElement>(
                    '.plugin-card header input:not(:disabled), .plugin-activation input:not(:disabled), .plugin-management button:not(:disabled)',
                  )
                if (destination) {
                  event.preventDefault()
                  destination.focus()
                  destination.scrollIntoView?.({ block: 'nearest' })
                }
                return
              }
              let next = index
              if (event.key === 'Home') next = 0
              else if (event.key === 'End') next = tabs.length - 1
              else if (event.key === 'ArrowLeft') next = (index - 1 + tabs.length) % tabs.length
              else if (event.key === 'ArrowRight') next = (index + 1) % tabs.length
              else return
              event.preventDefault()
              strip.current?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus()
              if (mode === 'desktop') select(tabs[next].id)
            }}
          >
            {tab.name}
          </button>
        ))}
      </div>
      {edges.overflow && (
        <button
          aria-label="Scroll plugin tabs right"
          disabled={edges.end}
          onClick={() => strip.current?.scrollBy({ left: strip.current.clientWidth * 0.7 })}
        >
          <ChevronRight aria-hidden="true" />
        </button>
      )}
    </div>
  )
}

export function PluginCard({ plugin, mode = 'desktop' }: { plugin: PluginSnapshot; mode?: Mode }) {
  const [ordinary, setOrdinary] = useViewState<Record<string, string>>(
    `${mode}:plugin:${plugin.id}:drafts`,
    {},
  )
  const [secrets, setSecrets] = useState<Record<string, string>>({})
  const [message, setMessage] = useState('')
  const [failed, setFailed] = useState(false)
  const [advanced, setAdvanced] = useState(false)
  const [missingKey, setMissingKey] = useState<string | null>(null)
  const form = useRef<HTMLFormElement>(null)
  const lock = useRef(false)
  const focusOrigin = useRef<HTMLElement | null>(null)
  const wasBusy = useRef(false)
  const client = useQueryClient()
  const busy = useIsMutating({ mutationKey: ['plugin-write', plugin.id] }) > 0
  useEffect(() => {
    const retained = Object.fromEntries(
      Object.entries(ordinary).filter(([key, value]) => {
        const field = plugin.settings.find((setting) => setting.key === key && !setting.isSecret)
        return field && value !== (field.value ?? (field.isBoolean ? 'false' : ''))
      }),
    )
    // Once the authoritative snapshot contains a draft, later remote edits may reach the form.
    if (Object.keys(retained).length !== Object.keys(ordinary).length) setOrdinary(retained)
  }, [ordinary, plugin.settings, setOrdinary])
  const command = useMutation({
    mutationKey: ['plugin-write', plugin.id],
    retry: false,
    mutationFn: async ({
      route,
      body,
      key,
      success,
      failure,
    }: {
      route: string
      body?: unknown
      key?: string
      success: string
      failure: string
    }) => {
      if (lock.current) return
      lock.current = true
      setFailed(false)
      setMessage('')
      try {
        await request(route, { pluginId: plugin.id, ...(key ? { key } : {}) }, body)
        setSecrets({})
        await client.invalidateQueries({ queryKey: ['api'] })
        setMessage(success)
      } catch {
        setFailed(true)
        setMessage(failure)
      } finally {
        lock.current = false
      }
    },
  })
  function run(input: Parameters<typeof command.mutate>[0]) {
    if (busy || lock.current) return
    focusOrigin.current = document.activeElement as HTMLElement | null
    command.mutate(input)
  }
  useLayoutEffect(() => {
    if (wasBusy.current && !busy) {
      const origin = focusOrigin.current
      if (origin?.isConnected && document.activeElement === document.body)
        origin.focus({ preventScroll: true })
      focusOrigin.current = null
    }
    wasBusy.current = busy
  }, [busy])
  useLayoutEffect(() => {
    if (missingKey)
      [...(form.current?.querySelectorAll<HTMLInputElement>('input') ?? [])]
        .find((input) => input.name === missingKey)
        ?.focus()
  }, [missingKey, advanced])
  const editable = plugin.canConfigure && !busy
  function save() {
    if (!editable || lock.current) return
    const missing = plugin.settings.find(
      (setting) =>
        setting.isRequired &&
        !(setting.isSecret && setting.hasStoredSecret) &&
        !(setting.isSecret ? secrets[setting.key] : (ordinary[setting.key] ?? setting.value))?.trim(),
    )
    if (missing) {
      if (missing.isAdvanced) setAdvanced(true)
      setMissingKey(missing.key)
      setFailed(true)
      setMessage(`Enter ${missing.label.toLowerCase()} before saving.`)
      return
    }
    const values = Object.fromEntries(
      plugin.settings.flatMap((setting) => {
        const value = setting.isSecret
          ? secrets[setting.key]
          : (ordinary[setting.key] ?? setting.value ?? (setting.isBoolean ? 'false' : ''))
        return setting.isSecret && !value?.trim() ? [] : [[setting.key, value]]
      }),
    )
    if (!Object.keys(values).length) {
      setMessage('Enter a setting or secret before saving.')
      return
    }
    setMissingKey(null)
    run({
      route: 'plugins.settings',
      body: { values },
      success: 'Provider settings saved.',
      failure:
        "Could not save plugin settings. Check that secure storage is available and Winnow's data folder is writable, then try again.",
    })
  }
  const renderField = (setting: PluginSetting) => (
    <label className={setting.isBoolean ? 'check-field' : 'field'} key={setting.key}>
      {!setting.isBoolean && setting.label}
      {setting.isBoolean ? (
        <input
          type="checkbox"
          disabled={!editable}
          name={setting.key}
          aria-label={`${plugin.name} ${setting.label}`}
          checked={(ordinary[setting.key] ?? setting.value)?.toLowerCase() === 'true'}
          onChange={(event) =>
            setOrdinary((values) => ({ ...values, [setting.key]: String(event.target.checked) }))
          }
        />
      ) : (
        <input
          type={setting.isSecret ? 'password' : 'text'}
          disabled={!editable}
          name={setting.key}
          aria-label={`${plugin.name} ${setting.label}`}
          aria-invalid={missingKey === setting.key || undefined}
          autoComplete="off"
          value={
            setting.isSecret ? (secrets[setting.key] ?? '') : (ordinary[setting.key] ?? setting.value ?? '')
          }
          placeholder={setting.hasStoredSecret ? 'Saved secret — leave blank to keep' : undefined}
          required={setting.isRequired && !setting.hasStoredSecret}
          onChange={(event) => {
            const value = event.target.value
            if (missingKey === setting.key) setMissingKey(null)
            if (setting.isSecret) setSecrets((values) => ({ ...values, [setting.key]: value }))
            else setOrdinary((values) => ({ ...values, [setting.key]: value }))
          }}
        />
      )}
      {setting.isBoolean && setting.label}
      {setting.description && <small>{setting.description}</small>}
      {pluginWebUrl(setting.setupUrl) && (
        <button
          type="button"
          aria-label={`Get ${plugin.name} ${setting.label}`}
          onClick={() => void openExternal(pluginWebUrl(setting.setupUrl)!)}
        >
          Get {setting.label.toLowerCase()}
        </button>
      )}
      {setting.isSecret && (
        <button
          type="button"
          disabled={!editable || !setting.hasStoredSecret}
          aria-label={`Remove saved ${plugin.name} ${setting.label}`}
          onClick={() => {
            setSecrets((values) => ({ ...values, [setting.key]: '' }))
            run({
              route: 'plugins.removeSecret',
              key: setting.key,
              success: 'Saved secret removed. Any configured fallback remains available.',
              failure:
                "Could not remove the saved secret. Check that Winnow's data folder is writable, then try again.",
            })
          }}
        >
          Remove saved {setting.label}
        </button>
      )}
    </label>
  )
  const activation = (
    <label className="check-field">
      <input
        type="checkbox"
        checked={plugin.enabled}
        disabled={!editable}
        aria-label={`${plugin.enabled ? 'Disable' : 'Enable'} plugin: ${plugin.name}`}
        onChange={(event) =>
          run({
            route: 'plugins.enabled',
            body: { enabled: event.target.checked },
            success:
              'Plugin setting saved. Choose Restart library service in Manage plugins to apply the change.',
            failure:
              "Could not change this plugin. Check that Winnow's data folder is writable, then try again.",
          })
        }
      />
      {plugin.enabled ? 'Enabled' : 'Disabled'}
    </label>
  )
  const web = pluginWebUrl(plugin.websiteUrl)
  return (
    <section className="feature-panel plugin-card" aria-label={`${plugin.name} settings`}>
      <header className="feature-heading">
        <div>
          <h2>{plugin.name}</h2>
          <small>
            v{plugin.version} · {plugin.capabilities}
          </small>
        </div>
        {mode !== 'fullscreen' && activation}
      </header>
      <p>{plugin.description}</p>
      {mode === 'fullscreen' && plugin.canConfigure && (
        <div className="plugin-activation">
          <h3>Activation</h3>
          {activation}
        </div>
      )}
      <p className="muted">
        {plugin.status}
        {plugin.restartRequired
          ? ' · Choose Restart library service in Manage plugins to apply this change.'
          : ''}
      </p>
      {web && (
        <button aria-label={`Open ${plugin.name} website`} onClick={() => void openExternal(web)}>
          Provider website
        </button>
      )}
      <form
        noValidate
        ref={form}
        onSubmit={(event) => {
          event.preventDefault()
          save()
        }}
      >
        {plugin.settings.filter((setting) => !setting.isAdvanced).map(renderField)}
        {plugin.settings.some((setting) => setting.isAdvanced) && (
          <button
            type="button"
            aria-expanded={advanced}
            aria-label={`${advanced ? 'Hide' : 'Show'} advanced settings: ${plugin.name}`}
            onClick={() => setAdvanced(!advanced)}
          >
            {advanced ? 'Hide' : 'Show'} advanced settings
          </button>
        )}
        {plugin.settings.some((setting) => setting.isSecret && (advanced || !setting.isAdvanced)) && (
          <p className="muted">{pluginSecretNote}</p>
        )}
        {advanced && plugin.settings.filter((setting) => setting.isAdvanced).map(renderField)}
        <div className="form-actions">
          {plugin.canConfigure && plugin.settings.length > 0 && (
            <button disabled={!editable} aria-label={`Save ${plugin.name} settings`}>
              Save provider settings
            </button>
          )}
          <button
            type="button"
            disabled={!editable || !plugin.enabled || !plugin.isLoaded}
            aria-label={`Refresh ${plugin.name}`}
            onClick={() =>
              run({
                route: 'plugins.refresh',
                success: 'Refresh queued.',
                failure:
                  'Could not queue a plugin refresh. Choose Restart library service in Manage plugins to try again.',
              })
            }
          >
            Refresh provider
          </button>
        </div>
      </form>
      {message && <p role={failed ? 'alert' : 'status'}>{message}</p>}
      {plugin.hasAccount && <PluginAccount plugin={plugin} busy={busy} />}
    </section>
  )
}
