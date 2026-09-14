/**
 * DeepSeek Harness Desktop — browser bundle for the desktop tools settings section.
 *
 * This file is a client bundle in the harness module format: a classic script that
 * registers a lazy factory with window.__ModuleLoader__. The factory returns the
 * Cordis client plugin (name / inject / apply) and requires React from the shell's
 * platform module table. Native capabilities (process control, plugin install,
 * log tail) travel over the WebView2 message bridge.
 */
(function () {
  if (typeof window === 'undefined' || !window.__ModuleLoader__) return

  window.__ModuleLoader__.load({
    id: 'dsh-desktop-tools',
    factory: function (require) {
      var React = require('react')
      var h = React.createElement

      // ===================== 原生桥接 =====================
      var bridgeAvailable = !!(window.chrome && window.chrome.webview)
      var pending = new Map()
      var seq = 1

      if (bridgeAvailable) {
        window.chrome.webview.addEventListener('message', function (event) {
          var msg = event.data
          if (!msg || msg.dsh !== 'desktop') return
          var entry = pending.get(msg.req)
          if (!entry) return
          pending.delete(msg.req)
          clearTimeout(entry.timer)
          if (msg.ok) entry.resolve(msg.data)
          else entry.reject(new Error(msg.error || '操作失败'))
        })
      }

      function call(action, payload, timeoutMs) {
        return new Promise(function (resolve, reject) {
          if (!bridgeAvailable) {
            reject(new Error('原生桥接不可用:请在 DeepSeek Harness Desktop 应用中使用该页面'))
            return
          }
          var req = seq++
          var timer = setTimeout(function () {
            pending.delete(req)
            reject(new Error('请求超时'))
          }, timeoutMs || 60000)
          pending.set(req, { resolve: resolve, reject: reject, timer: timer })
          window.chrome.webview.postMessage(Object.assign({ dsh: 'desktop', req: req, action: action }, payload || {}))
        })
      }

      // ===================== 样式(跟随 Harness 主题 token) =====================
      var CSS = [
        '.dt-root{display:flex;flex-direction:column;gap:14px;font-size:13px;color:var(--dsw-alias-label-primary);padding:2px 2px 24px;}',
        '.dt-title{font-size:16px;font-weight:600;}',
        '.dt-desc{color:var(--dsw-alias-label-caption);font-size:12px;line-height:1.7;margin-top:4px;}',
        '.dt-statusline{display:flex;align-items:center;gap:8px;color:var(--dsw-alias-label-secondary);font-size:12px;}',
        '.dt-url{color:var(--dsw-alias-label-caption);font-family:monospace;font-size:11.5px;}',
        '.dt-dot{width:8px;height:8px;border-radius:50%;background:var(--dsw-alias-label-caption);}',
        '.dt-dot[data-state="Running"]{background:rgb(34,197,94);}',
        '.dt-dot[data-state="Starting"],.dt-dot[data-state="Stopping"]{background:rgb(245,158,11);}',
        '.dt-dot[data-state="Failed"]{background:rgb(242,90,90);}',
        '.dt-tabs{display:flex;gap:6px;}',
        '.dt-tab{padding:6px 14px;border-radius:8px;border:1px solid transparent;background:transparent;color:var(--dsw-alias-label-secondary);cursor:pointer;font-size:12.5px;font-family:inherit;}',
        '.dt-tab:hover{background:var(--dsw-alias-interactive-bg-hover);}',
        '.dt-tab[data-active="true"]{background:var(--dsw-alias-bg-layer-3);color:var(--dsw-alias-label-primary);font-weight:600;}',
        '.dt-card{border:1px solid var(--dsw-alias-border-l2);border-radius:10px;padding:14px;}',
        '.dt-section{font-size:13px;font-weight:600;margin-bottom:8px;}',
        '.dt-row{display:flex;align-items:center;gap:10px;padding:8px 6px;border-radius:8px;}',
        '.dt-row:hover{background:var(--dsw-alias-interactive-bg-hover);}',
        '.dt-grow{flex:1;min-width:0;}',
        '.dt-name{font-weight:600;font-size:12.5px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}',
        '.dt-ver{color:var(--dsw-alias-label-caption);font-size:11px;font-family:monospace;margin-left:6px;}',
        '.dt-sub{color:var(--dsw-alias-label-caption);font-size:11px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;margin-top:2px;}',
        '.dt-badge{display:inline-block;padding:1px 7px;border-radius:999px;font-size:10px;background:var(--dsw-alias-bg-layer-3);color:var(--dsw-alias-label-caption);margin-left:6px;}',
        '.dt-btn{padding:6px 13px;border-radius:8px;border:1px solid var(--dsw-alias-border-l2);background:transparent;color:var(--dsw-alias-label-primary);cursor:pointer;font-size:12.5px;font-family:inherit;white-space:nowrap;}',
        '.dt-btn:hover{background:var(--dsw-alias-interactive-bg-hover);}',
        '.dt-btn[data-kind="primary"]{background:var(--dsw-alias-label-primary);color:var(--dsw-alias-label-primary-inverted);border-color:transparent;font-weight:600;}',
        '.dt-btn[data-kind="danger"]{color:rgb(242,90,90);border-color:rgba(242,90,90,0.35);}',
        '.dt-btn[data-kind="danger"]:hover{background:var(--dsw-alias-interactive-bg-hover-danger);}',
        '.dt-btn:disabled{opacity:.45;cursor:default;}',
        '.dt-input{flex:1;min-width:0;padding:7px 10px;border-radius:8px;border:1px solid var(--dsw-alias-border-l2);background:var(--dsw-alias-bg-layer-2);color:var(--dsw-alias-label-primary);font-size:12.5px;font-family:inherit;outline:none;}',
        '.dt-input:focus{border-color:var(--dsw-alias-brand-primary);}',
        '.dt-hint{color:var(--dsw-alias-label-caption);font-size:11px;margin-top:6px;}',
        '.dt-notice{border-radius:8px;padding:9px 12px;font-size:12px;line-height:1.5;}',
        '.dt-notice[data-kind="ok"]{background:rgba(34,197,94,0.12);color:rgb(78,209,126);}',
        '.dt-notice[data-kind="warn"]{background:rgba(245,158,11,0.12);color:rgb(247,173,49);}',
        '.dt-notice[data-kind="error"]{background:rgba(242,90,90,0.12);color:rgb(242,90,90);}',
        '.dt-busy{color:var(--dsw-alias-label-caption);font-size:12px;}',
        '.dt-output{font-family:monospace;font-size:11px;white-space:pre-wrap;word-break:break-all;color:var(--dsw-alias-label-secondary);background:var(--dsw-alias-bg-layer-1);border:1px solid var(--dsw-alias-border-l1);border-radius:8px;padding:10px;max-height:200px;overflow:auto;}',
        '.dt-log{font-family:monospace;font-size:11px;white-space:pre-wrap;word-break:break-all;color:var(--dsw-alias-label-secondary);background:var(--dsw-alias-bg-layer-1);border:1px solid var(--dsw-alias-border-l1);border-radius:8px;padding:10px;height:340px;overflow:auto;}',
        '.dt-log-line{display:flex;gap:8px;}',
        '.dt-log-time{color:var(--dsw-alias-label-caption);flex:none;}',
        '.dt-log-line[data-error="true"] .dt-log-text{color:rgb(242,90,90);}',
        '.dt-switch{accent-color:var(--dsw-alias-brand-primary);width:16px;height:16px;cursor:pointer;}',
        '.dt-actions{display:flex;gap:8px;flex-wrap:wrap;align-items:center;}',
        '.dt-kv{display:flex;gap:8px;font-size:12px;color:var(--dsw-alias-label-secondary);}',
        '.dt-kv-key{color:var(--dsw-alias-label-caption);flex:none;width:74px;}',
        '.dt-kv-val{font-family:monospace;font-size:11.5px;word-break:break-all;}',
      ].join('')

      function ensureStyles() {
        if (typeof document === 'undefined' || document.getElementById('dsh-desktop-tools-style')) return
        var el = document.createElement('style')
        el.id = 'dsh-desktop-tools-style'
        el.setAttribute('data-plugin', 'desktop-tools')
        el.textContent = CSS
        document.head.appendChild(el)
      }

      // ===================== 组件 =====================
      function stateText(state) {
        if (state === 'Running') return '服务运行中'
        if (state === 'Starting') return '服务启动中…'
        if (state === 'Stopping') return '服务停止中…'
        if (state === 'Failed') return '服务启动失败'
        return '服务未启动'
      }

      function DesktopToolsSection() {
        var useState = React.useState
        var useEffect = React.useEffect
        var useCallback = React.useCallback

        var [tab, setTab] = useState('plugins')
        var [info, setInfo] = useState(null)
        var [plugins, setPlugins] = useState([])
        var [busy, setBusy] = useState('')
        var [output, setOutput] = useState('')
        var [notice, setNotice] = useState(null)
        var [logs, setLogs] = useState([])
        var [autoLog, setAutoLog] = useState(true)
        var [port, setPort] = useState('')
        var [installSpec, setInstallSpec] = useState('')

        var refreshInfo = useCallback(function () {
          call('app.info').then(function (data) {
            setInfo(data)
            setPort(String(data.port))
          }).catch(function () {})
        }, [])

        var refreshPlugins = useCallback(function () {
          call('plugins.list').then(function (data) {
            setPlugins((data && data.items) || [])
          }).catch(function (e) {
            setNotice({ kind: 'error', text: e.message })
          })
        }, [])

        useEffect(function () {
          refreshInfo()
          refreshPlugins()
        }, [refreshInfo, refreshPlugins])

        useEffect(function () {
          if (tab !== 'logs' || !autoLog) return undefined
          var load = function () {
            call('logs.tail', { lines: 400 }).then(function (data) {
              setLogs((data && data.lines) || [])
            }).catch(function () {})
          }
          load()
          var timer = setInterval(load, 2000)
          return function () { clearInterval(timer) }
        }, [tab, autoLog])

        function run(label, promise) {
          setBusy(label + '…')
          setOutput('')
          return promise.then(function (data) {
            if (data && data.output) setOutput(data.output)
            if (data && data.ok === false) {
              setNotice({ kind: 'error', text: label + '失败,详见操作输出。' })
              return
            }
            if (data && data.needsRestart) {
              setNotice({ kind: 'warn', text: label + '完成。变更将在重启服务后生效。' })
            } else {
              setNotice({ kind: 'ok', text: label + '完成。' })
            }
            refreshPlugins()
            refreshInfo()
          }).catch(function (e) {
            setNotice({ kind: 'error', text: label + '失败:' + e.message })
          }).finally(function () {
            setBusy('')
          })
        }

        function serviceAction(action, label) {
          if ((action === 'service.stop' || action === 'service.restart') &&
              !window.confirm(label + '服务?当前页面会重新加载。')) return
          run(label, call(action, {}, 240000))
        }

        function onInstall() {
          var spec = installSpec.trim()
          if (!spec) return
          run('安装插件', call('plugins.install', { spec: spec }, 600000))
        }

        // ---------- 渲染工具 ----------
        function tabButton(id, text) {
          return h('button', {
            key: id,
            className: 'dt-tab',
            'data-active': tab === id ? 'true' : 'false',
            onClick: function () { setTab(id); setNotice(null) },
          }, text)
        }

        function renderPlugins() {
          return h('div', { className: 'dt-root' },
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-section' }, '安装插件'),
              h('div', { className: 'dt-actions' },
                h('input', {
                  className: 'dt-input',
                  placeholder: 'npm 包名 / 精确版本 / 本地目录 / git 仓库',
                  value: installSpec,
                  onChange: function (e) { setInstallSpec(e.target.value) },
                  onKeyDown: function (e) { if (e.key === 'Enter') onInstall() },
                }),
                h('button', {
                  className: 'dt-btn',
                  'data-kind': 'primary',
                  disabled: !!busy,
                  onClick: onInstall,
                }, '安装')
              ),
              h('div', { className: 'dt-hint' }, '通过官方 dsh plugin(内部调用 pnpm)安装;声明 dsh.bundle.patch 的包会作为 bundle 层加载。')
            ),
            h('div', { className: 'dt-card' },
              h('div', { style: { display: 'flex', alignItems: 'center', justifyContent: 'space-between' } },
                h('div', { className: 'dt-section', style: { marginBottom: 0 } }, '已安装插件 (' + plugins.length + ')'),
                h('button', {
                  className: 'dt-btn',
                  disabled: !!busy,
                  onClick: function () {
                    if (window.confirm('恢复默认插件集?这会停用所有第三方 bundle,仅保留内置插件。')) {
                      run('恢复默认插件集', call('plugins.reset'))
                    }
                  },
                }, '恢复默认')
              ),
              h('div', { style: { marginTop: 6 } },
                plugins.length === 0
                  ? h('div', { className: 'dt-hint' }, '尚未安装第三方插件。')
                  : plugins.map(function (p) {
                      return h('div', { className: 'dt-row', key: p.name },
                        h('div', { className: 'dt-grow' },
                          h('div', { className: 'dt-name' }, p.name,
                            h('span', { className: 'dt-ver' }, p.version),
                            p.builtIn ? h('span', { className: 'dt-badge' }, '内置') : null
                          ),
                          p.description ? h('div', { className: 'dt-sub' }, p.description) : null
                        ),
                        h('input', {
                          className: 'dt-switch',
                          type: 'checkbox',
                          checked: !!p.enabled,
                          disabled: !!busy || p.builtIn,
                          title: p.builtIn ? '内置 bundle 始终启用' : '启用 / 停用',
                          onChange: function (e) {
                            run((e.target.checked ? '启用' : '停用') + '插件', call('plugins.setEnabled', { name: p.name, enabled: e.target.checked }))
                          },
                        }),
                        h('button', {
                          className: 'dt-btn',
                          'data-kind': 'danger',
                          disabled: !!busy || p.builtIn,
                          onClick: function () {
                            if (window.confirm('确定要移除插件 ' + p.name + ' 吗?')) {
                              run('移除插件', call('plugins.remove', { name: p.name }, 600000))
                            }
                          },
                        }, '移除')
                      )
                    })
              ),
              h('div', { className: 'dt-actions', style: { marginTop: 12 } },
                h('button', { className: 'dt-btn', onClick: function () { call('app.openProfile').catch(function () {}) } }, '打开 Profile 目录')
              ),
            ),
            output ? h('div', { className: 'dt-output' }, output) : null
          )
        }

        function renderService() {
          var state = info ? info.state : 'Stopped'
          var running = state === 'Running'
          return h('div', { className: 'dt-root' },
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-actions' },
                h('span', { className: 'dt-dot', 'data-state': state }),
                h('span', { style: { fontWeight: 600 } }, stateText(state)),
                info && info.url ? h('span', { className: 'dt-url' }, info.url) : null
              ),
              h('div', { className: 'dt-actions', style: { marginTop: 12 } },
                h('button', {
                  className: 'dt-btn',
                  'data-kind': 'primary',
                  disabled: !!busy || state === 'Starting' || state === 'Stopping',
                  onClick: function () { serviceAction(running ? 'service.stop' : 'service.start', running ? '停止' : '启动') },
                }, running ? '停止服务' : '启动服务'),
                h('button', {
                  className: 'dt-btn',
                  disabled: !!busy || !running,
                  onClick: function () { serviceAction('service.restart', '重启') },
                }, '重启'),
                h('button', {
                  className: 'dt-btn',
                  disabled: !running,
                  onClick: function () { call('app.openBrowser').catch(function () {}) },
                }, '在浏览器打开')
              )
            ),
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-section' }, '监听端口'),
              h('div', { className: 'dt-actions' },
                h('input', {
                  className: 'dt-input',
                  style: { maxWidth: 140 },
                  value: port,
                  onChange: function (e) { setPort(e.target.value.replace(/[^0-9]/g, '')) },
                }),
                h('button', {
                  className: 'dt-btn',
                  disabled: !!busy,
                  onClick: function () { run('保存端口', call('service.setPort', { port: Number(port) }, 240000)) },
                }, '保存并重启')
              ),
              h('div', { className: 'dt-hint' }, '默认 3080;填 0 由系统分配空闲端口。')
            ),
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-section' }, '启动行为'),
              behaviorRow('启动应用时自动启动服务', 'autoStart'),
              behaviorRow('禁用遥测上报', 'telemetryDisabled'),
              behaviorRow('关闭窗口时最小化到任务栏', 'closeToTray')
            ),
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-section' }, '数据与诊断'),
              info ? h('div', { style: { display: 'flex', flexDirection: 'column', gap: 6 } },
                kv('数据目录', info.dshHome),
                kv('Profile', info.profileDir),
                kv('运行时', info.runtimeDir)
              ) : null,
              h('div', { className: 'dt-actions', style: { marginTop: 12 } },
                h('button', { className: 'dt-btn', onClick: function () { call('app.openDataDir').catch(function () {}) } }, '打开数据目录'),
                h('button', { className: 'dt-btn', onClick: function () { call('app.openLog').catch(function () {}) } }, '打开应用日志'),
                h('button', { className: 'dt-btn', onClick: function () { call('app.openRuntime').catch(function () {}) } }, '打开运行时目录')
              ),
              info ? h('div', { className: 'dt-hint', style: { marginTop: 10 } },
                '客户端 ' + info.version + ' · 运行时 @deepseek-ai/dsh ' + info.dshVersion) : null
            )
          )
        }

        function behaviorRow(text, key) {
          var checked = !!(info && info[key])
          return h('div', { className: 'dt-row' },
            h('div', { className: 'dt-grow' }, text),
            h('input', {
              className: 'dt-switch',
              type: 'checkbox',
              checked: checked,
              onChange: function (e) {
                var patch = {}
                patch[key] = e.target.checked
                call('service.setBehavior', patch).then(refreshInfo).catch(function (err) {
                  setNotice({ kind: 'error', text: err.message })
                })
              },
            })
          )
        }

        function kv(key, value) {
          return h('div', { className: 'dt-kv', key: key },
            h('span', { className: 'dt-kv-key' }, key),
            h('span', { className: 'dt-kv-val' }, value || '—')
          )
        }

        function renderLogs() {
          return h('div', { className: 'dt-root' },
            h('div', { className: 'dt-actions' },
              h('label', { style: { display: 'flex', alignItems: 'center', gap: 6, cursor: 'pointer', color: 'var(--dsw-alias-label-secondary)', fontSize: 12 } },
                h('input', {
                  className: 'dt-switch',
                  type: 'checkbox',
                  checked: autoLog,
                  onChange: function (e) { setAutoLog(e.target.checked) },
                }),
                '自动刷新'
              ),
              h('button', {
                className: 'dt-btn',
                onClick: function () {
                  call('logs.tail', { lines: 400 }).then(function (data) { setLogs((data && data.lines) || []) }).catch(function () {})
                },
              }, '刷新'),
              h('button', {
                className: 'dt-btn',
                onClick: function () {
                  var text = logs.map(function (line) { return '[' + line.t + '] ' + line.text }).join('\n')
                  if (navigator.clipboard) navigator.clipboard.writeText(text)
                },
              }, '复制'),
              h('button', {
                className: 'dt-btn',
                'data-kind': 'danger',
                onClick: function () { call('logs.clear').then(function () { setLogs([]) }).catch(function () {}) },
              }, '清空')
            ),
            h('div', { className: 'dt-log' },
              logs.length === 0
                ? h('div', { className: 'dt-hint' }, '暂无日志。')
                : logs.map(function (line, index) {
                    return h('div', { className: 'dt-log-line', key: index, 'data-error': line.error ? 'true' : 'false' },
                      h('span', { className: 'dt-log-time' }, line.t),
                      h('span', { className: 'dt-log-text' }, line.text)
                    )
                  })
            )
          )
        }

        return h('div', { className: 'dt-root', style: { maxHeight: '100%', overflow: 'auto' } },
          h('div', null,
            h('div', { className: 'dt-title' }, '桌面工具'),
            h('div', { className: 'dt-desc' }, '由 DeepSeek Harness Desktop 提供:管理本机 Harness 插件、服务进程与运行日志。')
          ),
          info
            ? h('div', { className: 'dt-statusline' },
                h('span', { className: 'dt-dot', 'data-state': info.state }),
                h('span', null, stateText(info.state)),
                info.url ? h('span', { className: 'dt-url' }, info.url) : null
              )
            : null,
          h('div', { className: 'dt-tabs' }, tabButton('plugins', '插件'), tabButton('service', '服务'), tabButton('logs', '日志')),
          notice ? h('div', { className: 'dt-notice', 'data-kind': notice.kind }, notice.text) : null,
          busy ? h('div', { className: 'dt-busy' }, busy) : null,
          tab === 'plugins' ? renderPlugins() : null,
          tab === 'service' ? renderService() : null,
          tab === 'logs' ? renderLogs() : null
        )
      }

      // ===================== 插件定义 =====================
      function apply(ctx) {
        ensureStyles()
        ctx.slots.inject('settings.section', function () {
          return ctx.slots.register({
            name: 'settings.section',
            id: 'desktop-tools',
            order: 90,
            label: '桌面工具',
          }, DesktopToolsSection)
        })
      }

      return { name: 'dsh-desktop-tools', inject: ['slots'], apply: apply }
    },
  })
})()
