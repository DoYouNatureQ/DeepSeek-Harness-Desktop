/**
 * DeepSeek Harness Desktop — browser bundle for the desktop settings sections.
 *
 * This file is a client bundle in the harness module format: a classic script that
 * registers a lazy factory with window.__ModuleLoader__. The factory returns the
 * Cordis client plugin (name / inject / apply) and requires React from the shell's
 * platform module table. Native capabilities (process control, plugin install,
 * log tail) travel over the WebView2 message bridge.
 *
 * Two settings pages live here:
 * - 桌面工具 (id `desktop-tools`): plugin inventory, service process, runtime log.
 * - 使用统计 (id `usage`): a contribution-style daily token heatmap plus summary
 *   cards and recent sessions. It reads the harness session RPCs
 *   (`session/list`, `session/follow`, `session/page`) through the `remote`
 *   client service, folds each session's own `assistant/message` usage samples
 *   into local calendar days, and caches the result in localStorage.
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
        // ---------- 使用统计 ----------
        '.dt-usage-toolbar{display:flex;align-items:center;gap:8px;flex-wrap:wrap;}',
        '.dt-usage-status{color:var(--dsw-alias-label-caption);font-size:11.5px;}',
        '.dt-cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(132px,1fr));gap:10px;}',
        '.dt-card-tight{border:1px solid var(--dsw-alias-border-l2);border-radius:10px;padding:12px;}',
        '.dt-stat-label{color:var(--dsw-alias-label-caption);font-size:11.5px;}',
        '.dt-stat-value{font-size:20px;font-weight:600;margin-top:6px;font-variant-numeric:tabular-nums;}',
        '.dt-stat-sub{color:var(--dsw-alias-label-caption);font-size:11px;margin-top:4px;}',
        '.dt-heat{--dt-cell:13px;--dt-gap:3px;display:flex;flex-direction:column;gap:6px;overflow-x:auto;padding-bottom:4px;}',
        '.dt-heat-months{display:grid;grid-auto-flow:column;gap:var(--dt-gap);margin-left:26px;}',
        '.dt-heat-month{color:var(--dsw-alias-label-caption);font-size:10.5px;white-space:nowrap;overflow:visible;}',
        '.dt-heat-body{display:flex;gap:6px;}',
        '.dt-heat-weekdays{display:grid;grid-template-rows:repeat(7,var(--dt-cell));gap:var(--dt-gap);}',
        '.dt-heat-weekday{color:var(--dsw-alias-label-caption);font-size:10px;line-height:var(--dt-cell);width:20px;}',
        '.dt-heat-grid{display:grid;grid-auto-flow:column;grid-template-rows:repeat(7,var(--dt-cell));gap:var(--dt-gap);}',
        '.dt-heat-cell{width:var(--dt-cell);height:var(--dt-cell);border-radius:3px;background:var(--dsw-alias-bg-layer-3);}',
        '.dt-heat-cell[data-level="1"]{background:rgba(34,197,94,0.28);}',
        '.dt-heat-cell[data-level="2"]{background:rgba(34,197,94,0.48);}',
        '.dt-heat-cell[data-level="3"]{background:rgba(34,197,94,0.72);}',
        '.dt-heat-cell[data-level="4"]{background:rgb(34,197,94);}',
        '.dt-heat-cell[data-future="true"]{background:transparent;}',
        '.dt-heat-cell[data-active="true"]{outline:1px solid var(--dsw-alias-label-primary);outline-offset:1px;}',
        '.dt-heat-foot{display:flex;align-items:center;gap:10px;flex-wrap:wrap;}',
        '.dt-heat-legend{display:flex;align-items:center;gap:5px;color:var(--dsw-alias-label-caption);font-size:11px;margin-left:auto;}',
        '.dt-heat-legend .dt-heat-cell{width:11px;height:11px;}',
        '.dt-heat-detail{color:var(--dsw-alias-label-secondary);font-size:12px;line-height:1.7;min-height:20px;}',
        '.dt-heat-detail b{color:var(--dsw-alias-label-primary);font-weight:600;}',
        '.dt-session-row{display:flex;align-items:center;gap:10px;padding:7px 6px;border-radius:8px;}',
        '.dt-session-row:hover{background:var(--dsw-alias-interactive-bg-hover);}',
        '.dt-session-meta{color:var(--dsw-alias-label-caption);font-size:11px;font-variant-numeric:tabular-nums;white-space:nowrap;}',
        '.dt-session-tokens{font-size:12px;font-weight:600;font-variant-numeric:tabular-nums;white-space:nowrap;}',
        '.dt-footnote{color:var(--dsw-alias-label-caption);font-size:11px;line-height:1.7;}',
      ].join('')

      function ensureStyles() {
        if (typeof document === 'undefined') return
        var el = document.getElementById('dsh-desktop-tools-style')
        if (el === null) {
          el = document.createElement('style')
          el.id = 'dsh-desktop-tools-style'
          // 与包名一致:HMR 重载时只清理 data-plugin=<包名> 的样式标签。
          el.setAttribute('data-plugin', 'dsh-desktop-tools')
          document.head.appendChild(el)
        }
        // 每次 apply 都重写内容,热重载后样式同样生效(不必刷新页面)。
        el.textContent = CSS
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

      // ===================== 用量统计:数据层 =====================
      // 数据来自 Harness 会话事件日志:每条 assistant/message 带着该步的 token
      // 用量与落盘时间,按本地自然日汇总即可得到贡献图。全程在页面内计算,
      // 结果缓存在 localStorage(按会话 id + updatedAt 失效)。
      var USAGE_CACHE_KEY = 'dsh-desktop-usage-cache-v1'
      var USAGE_CACHE_VERSION = 1
      var USAGE_PAGE_MESSAGES = 400
      var USAGE_PAGE_LIMIT = 40
      var USAGE_CONCURRENCY = 4
      var USAGE_RECENT_LIMIT = 8
      var WEEKDAY_SHORT = ['一', '', '三', '', '五', '', '日']
      var WEEKDAY_FULL = ['周一', '周二', '周三', '周四', '周五', '周六', '周日']

      function pad2(value) { return (value < 10 ? '0' : '') + value }

      function dayKeyOf(time) {
        var date = new Date(time)
        return date.getFullYear() + '-' + pad2(date.getMonth() + 1) + '-' + pad2(date.getDate())
      }

      function startOfDay(date) { return new Date(date.getFullYear(), date.getMonth(), date.getDate()) }
      function addDays(date, days) { return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days) }
      function startOfWeek(date) {
        var day = startOfDay(date)
        return addDays(day, -((day.getDay() + 6) % 7))
      }

      function formatTokens(value) {
        var amount = Math.round(value || 0)
        if (amount <= 0) return '0'
        if (amount >= 1000000000) return (amount / 1000000000).toFixed(2) + 'B'
        if (amount >= 1000000) return (amount / 1000000).toFixed(amount >= 10000000 ? 0 : 1) + 'M'
        if (amount >= 1000) return (amount / 1000).toFixed(amount >= 10000 ? 0 : 1) + 'K'
        return String(amount)
      }

      function formatCount(value) {
        return String(Math.round(value || 0)).replace(/\B(?=(\d{3})+(?!\d))/g, ',')
      }

      function formatDayLabel(dayKey) {
        var parts = String(dayKey).split('-')
        return Number(parts[1]) + '月' + Number(parts[2]) + '日'
      }

      function formatDateTime(time) {
        var date = new Date(time)
        return (date.getMonth() + 1) + '月' + date.getDate() + '日 ' + pad2(date.getHours()) + ':' + pad2(date.getMinutes())
      }

      function emptyBucket() { return { t: 0, i: 0, o: 0, cr: 0, cw: 0, steps: 0, prompts: 0 } }

      function dayBucket(dayMap, key) {
        var bucket = dayMap[key]
        if (bucket === undefined) { bucket = emptyBucket(); dayMap[key] = bucket }
        return bucket
      }

      function tokenCount(value) {
        return typeof value === 'number' && isFinite(value) && value > 0 ? value : 0
      }

      function usageBucket(usage) {
        var input = tokenCount(usage.inputTokens)
        var output = tokenCount(usage.outputTokens)
        var cacheRead = tokenCount(usage.cacheReadTokens)
        var cacheWrite = tokenCount(usage.cacheWriteTokens)
        return { t: input + output + cacheRead + cacheWrite, i: input, o: output, cr: cacheRead, cw: cacheWrite, steps: 0, prompts: 0 }
      }

      function addBucket(target, source) {
        target.t += source.t
        target.i += source.i
        target.o += source.o
        target.cr += source.cr
        target.cw += source.cw
        target.steps += source.steps
        target.prompts += source.prompts
      }

      function subBucket(target, source) {
        target.t -= source.t
        target.i -= source.i
        target.o -= source.o
        target.cr -= source.cr
        target.cw -= source.cw
      }

      /** 取出一次模型调用的用量:assistant/message 自带 usage,
       *  未提交的 assistant/attempt 只能从 compact stream 的最后一个 usage 块里读。 */
      function usageOfEvent(event) {
        if (event.type === 'assistant/message' && event.data && event.data.usage) return event.data.usage
        if (event.type !== 'assistant/message' && event.type !== 'assistant/attempt') return undefined
        var stream = event.data && event.data.stream
        if (!stream || !stream.length) return undefined
        for (var index = stream.length - 1; index >= 0; index--) {
          var record = stream[index]
          if (record && record.type === 'chunk' && record.chunk && record.chunk.type === 'usage' && record.chunk.usage) {
            return record.chunk.usage
          }
        }
        return undefined
      }

      /**
       * 折叠一个会话的事件日志,复刻 Harness token-meter 的口径:
       * 同一 (turn, step) 的后一次用量上报替换前一次,llm/retry-started 关掉替换槽,
       * 于是被重试的那次调用照常计入总量。返回按自然日分桶的用量与总计。
       */
      function foldUsage(events) {
        var days = {}
        var totals = emptyBucket()
        var last = null
        var ordered = events.slice().sort(function (left, right) { return (left.seq || 0) - (right.seq || 0) })
        for (var index = 0; index < ordered.length; index++) {
          var event = ordered[index]
          if (!event || typeof event.type !== 'string') continue
          var key = dayKeyOf(typeof event.time === 'number' ? event.time : Date.now())
          if (event.type === 'step/end') {
            dayBucket(days, key).steps++
            totals.steps++
            continue
          }
          if (event.type === 'user/message' && event.data && event.data.source && event.data.source.kind === 'user') {
            dayBucket(days, key).prompts++
            totals.prompts++
            continue
          }
          if (event.type === 'llm/retry-started') {
            if (last !== null && event.data && last.turn === event.data.turn && last.step === event.data.step) last = null
            continue
          }
          if (event.type !== 'assistant/message' && event.type !== 'assistant/attempt') continue
          if (!event.data || typeof event.data.turn !== 'number') continue
          var usage = usageOfEvent(event)
          if (usage === undefined) continue
          if (last !== null && last.turn === event.data.turn && last.step === event.data.step) {
            subBucket(dayBucket(days, last.day), last.sample)
            subBucket(totals, last.sample)
            last = null
          }
          var sample = usageBucket(usage)
          addBucket(dayBucket(days, key), sample)
          addBucket(totals, sample)
          last = { turn: event.data.turn, step: event.data.step, day: key, sample: sample }
        }
        return { days: days, totals: totals }
      }

      function titleOfSession(row) {
        var projections = row.projections && row.projections.values
        var title = projections && typeof projections.title === 'string' ? projections.title : ''
        if (title) return title
        if (typeof row.cwd === 'string' && row.cwd) {
          var segments = row.cwd.split(/[\\/]/).filter(function (segment) { return !!segment })
          if (segments.length) return segments[segments.length - 1]
        }
        return String(row.sessionId || '').replace(/^session-/, '').slice(0, 12)
      }

      function isZeroUsageRow(row) {
        var projections = row.projections && row.projections.values
        var totals = projections && projections.tokenUsage
        if (!totals || row.running === true) return false
        return !tokenCount(totals.uncachedInputTokens) && !tokenCount(totals.outputTokens)
          && !tokenCount(totals.cacheReadTokens) && !tokenCount(totals.cacheWriteTokens)
      }

      /** 读取会话事件日志的扫描器:先 follow 拿到打开游标与首屏,再向前翻页直到尽头。 */
      function createUsageScanner(ctx) {
        /**
         * 取 Harness 会话接口。`remote.session` 只在会话功能挂载后才存在,把它写进 inject
         * 会让整个插件(含「桌面工具」页)一起被挂起,所以按 dsh 的约定把它当**可选服务**,
         * 用不要求 inject 的 ctx.get 读取;下面的属性访问只是兜底。
         * 任何一步取不到都退化成页内中文提示,而不是把框架原始错误抛到界面上。
         */
        function lookupSession() {
          try {
            if (ctx && typeof ctx.get === 'function') {
              var stored = ctx.get('remote.session')
              if (stored && typeof stored.list === 'function') return stored
            }
          } catch (error) { /* 命名空间未挂载 */ }
          try {
            if (ctx && ctx.remote) {
              var nested = ctx.remote.session
              if (nested && typeof nested.list === 'function') return nested
            }
          } catch (error) { /* 未把 remote 写进 inject */ }
          try {
            if (ctx) {
              var flat = ctx['remote.session']
              if (flat && typeof flat.list === 'function') return flat
            }
          } catch (error) { /* 未把 remote.session 写进 inject */ }
          return undefined
        }

        function sessionRemote() {
          var session = lookupSession()
          if (!session || typeof session.list !== 'function' || typeof session.page !== 'function') {
            throw new Error('当前页面取不到 Harness 会话接口(remote.session),无法统计用量')
          }
          return session
        }

        function errorText(result, fallback) {
          var error = result && result.error
          if (error && typeof error.message === 'string' && error.message) return error.message
          if (error && typeof error.code === 'string' && error.code) return fallback + '(' + error.code + ')'
          return fallback
        }

        async function listSessions(signal) {
          var result = await sessionRemote().list({}, signal)
          if (!result || result.ok !== true) throw new Error(errorText(result, '读取会话列表失败'))
          return (result.value && result.value.items) || []
        }

        function eventsOf(records) {
          var events = []
          for (var index = 0; index < (records || []).length; index++) {
            var record = records[index]
            if (record && record.event) events.push(record.event)
          }
          return events
        }

        async function readEvents(address, signal, onNote) {
          var session = sessionRemote()
          var inner = typeof AbortController === 'function' ? new AbortController() : undefined
          var forwardAbort = function () { if (inner) inner.abort() }
          if (signal) {
            if (signal.aborted) forwardAbort()
            else signal.addEventListener('abort', forwardAbort)
          }
          var snapshot
          var stream
          try {
            stream = session.follow({ address: address, maxMessages: USAGE_PAGE_MESSAGES }, inner ? inner.signal : signal)
            for await (var frame of stream) {
              if (frame && frame.type === 'snapshot') { snapshot = frame; break }
            }
          } finally {
            forwardAbort()
            if (signal) signal.removeEventListener('abort', forwardAbort)
          }
          var events = snapshot ? eventsOf(snapshot.records) : []
          var cursor = snapshot && typeof snapshot.cursor === 'number' ? snapshot.cursor : -1
          var hasMore = !!(snapshot && snapshot.hasMore)
          var pages = 1
          while (hasMore && pages < USAGE_PAGE_LIMIT) {
            if (signal && signal.aborted) break
            var firstSeq = events.length ? events[0].seq : undefined
            if (typeof firstSeq !== 'number' || firstSeq <= 0) break
            var result = await session.page({
              address: address,
              throughSeq: cursor,
              beforeSeq: firstSeq,
              maxMessages: USAGE_PAGE_MESSAGES,
            }, signal)
            if (!result || result.ok !== true) throw new Error(errorText(result, '读取会话历史失败'))
            var pageEvents = eventsOf(result.value && result.value.records)
            if (!pageEvents.length) break
            events = pageEvents.concat(events)
            hasMore = !!(result.value && result.value.hasMore)
            pages++
          }
          if (hasMore && onNote) onNote()
          return events
        }

        function addressFor(row) {
          if (row.origin === 'subagent') {
            var identity = row.projections && row.projections.values && row.projections.values.subagent
            if (!identity || (identity.mode !== 'one-shot' && identity.mode !== 'continuable')) return undefined
            return { kind: 'subagent', parentSessionId: row.parentSessionId, childSessionId: row.sessionId, mode: identity.mode }
          }
          return { kind: 'session', sessionId: row.sessionId }
        }

        function readCache() {
          try {
            var raw = window.localStorage.getItem(USAGE_CACHE_KEY)
            if (!raw) return { version: USAGE_CACHE_VERSION, sessions: {} }
            var parsed = JSON.parse(raw)
            if (!parsed || parsed.version !== USAGE_CACHE_VERSION || typeof parsed.sessions !== 'object') {
              return { version: USAGE_CACHE_VERSION, sessions: {} }
            }
            return parsed
          } catch (error) {
            return { version: USAGE_CACHE_VERSION, sessions: {} }
          }
        }

        function writeCache(cache) {
          try { window.localStorage.setItem(USAGE_CACHE_KEY, JSON.stringify(cache)) } catch (error) {
            // 缓存写不进去(配额/隐私模式)时按无缓存继续,不影响本次结果。
          }
        }

        function cachedSession(entry) {
          if (!entry || typeof entry !== 'object' || !entry.session) return undefined
          return entry.session
        }

        /**
         * 扫描全部会话。
         * @param options.signal 取消信号。
         * @param options.force 忽略缓存。
         * @param options.onProgress (done, total) 进度回调。
         * @param options.onSession 每完成一个会话回调一次,便于边扫边画。
         */
        async function scan(options) {
          var settings = options || {}
          var signal = settings.signal
          var rows = await listSessions(signal)
          var cache = readCache()
          var pending = []
          var skipped = 0
          var failed = 0
          var failureMessage = ''
          var truncated = 0
          var sessions = []
          var now = Date.now()
          for (var index = 0; index < rows.length; index++) {
            var row = rows[index]
            if (!row || !row.sessionId) continue
            var cached = cachedSession(cache.sessions[row.sessionId])
            // 运行中的会话 updatedAt 只跟随提问时间,长回合里会落后于日志,因此每次都重扫。
            if (settings.force !== true && row.running !== true && cached !== undefined && cache.sessions[row.sessionId].u === row.updatedAt) {
              sessions.push(cached)
              continue
            }
            if (isZeroUsageRow(row) && cached === undefined) {
              sessions.push({
                id: row.sessionId,
                title: titleOfSession(row),
                cwd: row.cwd,
                updatedAt: row.updatedAt,
                days: {},
                totals: emptyBucket(),
                truncated: false,
                scannedAt: now,
              })
              continue
            }
            var address = addressFor(row)
            if (address === undefined) { skipped++; continue }
            pending.push({ row: row, address: address })
          }
          var total = pending.length
          var done = 0
          if (typeof settings.onProgress === 'function') settings.onProgress(done, total)
          var nextIndex = 0
          var aborted = false
          async function worker() {
            while (!aborted) {
              var current = nextIndex++
              if (current >= pending.length) return
              if (signal && signal.aborted) { aborted = true; return }
              var item = pending[current]
              var note = false
              try {
                var events = await readEvents(item.address, signal, function () { note = true })
                var folded = foldUsage(events)
                var session = {
                  id: item.row.sessionId,
                  title: titleOfSession(item.row),
                  cwd: item.row.cwd,
                  updatedAt: item.row.updatedAt,
                  days: folded.days,
                  totals: folded.totals,
                  truncated: note,
                  scannedAt: Date.now(),
                }
                if (note) truncated++
                sessions.push(session)
                cache.sessions[item.row.sessionId] = { u: item.row.updatedAt, session: session }
                if (typeof settings.onSession === 'function') settings.onSession(session)
              } catch (failure) {
                if (signal && signal.aborted) { aborted = true; return }
                failed++
                if (!failureMessage) failureMessage = failure && failure.message ? failure.message : String(failure)
              }
              done++
              if (typeof settings.onProgress === 'function') settings.onProgress(done, total)
            }
          }
          var workers = []
          for (var slot = 0; slot < Math.min(USAGE_CONCURRENCY, pending.length); slot++) workers.push(worker())
          await Promise.all(workers)
          if (aborted) return { sessions: sessions, skipped: skipped, failed: failed, failureMessage: failureMessage, truncated: truncated, scannedAt: now, aborted: true }
          var live = {}
          for (var keep = 0; keep < sessions.length; keep++) live[sessions[keep].id] = true
          var keys = Object.keys(cache.sessions)
          for (var key = 0; key < keys.length; key++) {
            if (live[keys[key]] !== true) delete cache.sessions[keys[key]]
          }
          writeCache(cache)
          sessions.sort(function (left, right) { return (right.updatedAt || 0) - (left.updatedAt || 0) })
          return { sessions: sessions, skipped: skipped, failed: failed, failureMessage: failureMessage, truncated: truncated, scannedAt: Date.now(), aborted: false }
        }

        return { scan: scan }
      }

      // ===================== 用量统计:视图 =====================
      /** 会话列表 → 全量按日聚合(累计卡片与热力图共用一份数据)。 */
      function aggregateSessions(sessions) {
        var byDay = {}
        var totals = emptyBucket()
        for (var index = 0; index < sessions.length; index++) {
          var session = sessions[index]
          var keys = Object.keys(session.days || {})
          for (var key = 0; key < keys.length; key++) {
            var bucket = session.days[keys[key]]
            var cell = byDay[keys[key]]
            if (cell === undefined) {
              cell = emptyBucket()
              cell.sessions = 0
              byDay[keys[key]] = cell
            }
            addBucket(cell, bucket)
            if (bucket.t > 0 || bucket.steps > 0) cell.sessions++
            addBucket(totals, bucket)
          }
        }
        return { byDay: byDay, totals: totals, sessionCount: sessions.length }
      }

      function sumDays(byDay, fromTime, toTime) {
        var total = emptyBucket()
        var day = startOfDay(new Date(fromTime))
        var end = startOfDay(new Date(toTime))
        while (day.getTime() <= end.getTime()) {
          var bucket = byDay[dayKeyOf(day.getTime())]
          if (bucket !== undefined) addBucket(total, bucket)
          day = addDays(day, 1)
        }
        return total
      }

      function quantileOf(sortedValues, ratio) {
        if (!sortedValues.length) return 0
        var index = Math.min(sortedValues.length - 1, Math.max(0, Math.ceil(sortedValues.length * ratio) - 1))
        return sortedValues[index]
      }

      function levelOf(value, thresholds, max) {
        if (!value) return 0
        if (max > 0 && value >= max) return 4
        if (value <= thresholds[0]) return 1
        if (value <= thresholds[1]) return 2
        if (value <= thresholds[2]) return 3
        return 4
      }

      /** 生成贡献图窗口:列为周(周一起),行为周一至周日。 */
      function buildHeatWindow(aggregated, weeks) {
        var today = startOfDay(new Date())
        var firstDay = addDays(startOfWeek(today), -(weeks - 1) * 7)
        var cells = []
        var values = []
        var max = 0
        for (var week = 0; week < weeks; week++) {
          var column = []
          for (var weekday = 0; weekday < 7; weekday++) {
            var day = addDays(firstDay, week * 7 + weekday)
            var key = dayKeyOf(day.getTime())
            var future = day.getTime() > today.getTime()
            var bucket = future ? undefined : aggregated.byDay[key]
            var tokens = bucket ? bucket.t : 0
            if (tokens > 0) {
              values.push(tokens)
              if (tokens > max) max = tokens
            }
            column.push({ key: key, day: day, future: future, bucket: bucket, tokens: tokens })
          }
          cells.push(column)
        }
        values.sort(function (left, right) { return left - right })
        var thresholds = [quantileOf(values, 0.25), quantileOf(values, 0.5), quantileOf(values, 0.75)]
        for (var columnIndex = 0; columnIndex < cells.length; columnIndex++) {
          for (var rowIndex = 0; rowIndex < 7; rowIndex++) {
            cells[columnIndex][rowIndex].level = levelOf(cells[columnIndex][rowIndex].tokens, thresholds, max)
          }
        }
        return { columns: cells, max: max }
      }

      function createUsageSection(scanner) {
        return function UsageSection() {
          var useState = React.useState
          var useEffect = React.useEffect
          var useMemo = React.useMemo
          var useRef = React.useRef

          var [weeks, setWeeks] = useState(26)
          var [attempt, setAttempt] = useState(0)
          var [status, setStatus] = useState('loading')
          var [error, setError] = useState('')
          var [sessions, setSessions] = useState([])
          var [progress, setProgress] = useState({ done: 0, total: 0 })
          var [report, setReport] = useState(null)
          var [hoverKey, setHoverKey] = useState('')
          var runRef = useRef(0)

          useEffect(function () {
            var run = runRef.current + 1
            runRef.current = run
            var controller = typeof AbortController === 'function' ? new AbortController() : undefined
            setStatus('loading')
            setError('')
            setSessions([])
            setProgress({ done: 0, total: 0 })
            setReport(null)
            scanner.scan({
              force: attempt > 0,
              signal: controller ? controller.signal : undefined,
              onProgress: function (done, total) {
                if (runRef.current === run) setProgress({ done: done, total: total })
              },
              onSession: function (session) {
                if (runRef.current !== run) return
                setSessions(function (previous) {
                  var next = previous.filter(function (item) { return item.id !== session.id })
                  next.push(session)
                  next.sort(function (left, right) { return (right.updatedAt || 0) - (left.updatedAt || 0) })
                  return next
                })
              },
            }).then(function (result) {
              if (runRef.current !== run || result.aborted) return
              setSessions(result.sessions)
              setReport(result)
              setStatus('ready')
            }).catch(function (failure) {
              if (runRef.current !== run) return
              setError(failure && failure.message ? failure.message : String(failure))
              setStatus('error')
            })
            return function () {
              runRef.current++
              if (controller) controller.abort()
            }
          }, [attempt, scanner])

          var aggregated = useMemo(function () { return aggregateSessions(sessions) }, [sessions])
          var heat = useMemo(function () { return buildHeatWindow(aggregated, weeks) }, [aggregated, weeks])
          var todayKey = dayKeyOf(Date.now())
          var cards = useMemo(function () {
            var today = startOfDay(new Date())
            return [
              { label: '今日', bucket: sumDays(aggregated.byDay, today.getTime(), today.getTime()) },
              { label: '近 7 天', bucket: sumDays(aggregated.byDay, addDays(today, -6).getTime(), today.getTime()) },
              { label: '近 30 天', bucket: sumDays(aggregated.byDay, addDays(today, -29).getTime(), today.getTime()) },
              { label: '累计', bucket: aggregated.totals, note: aggregated.sessionCount + ' 个会话' },
            ]
          }, [aggregated])
          var recent = useMemo(function () {
            return sessions.slice().sort(function (left, right) {
              return (right.totals && right.totals.t ? right.totals.t : 0) - (left.totals && left.totals.t ? left.totals.t : 0)
            }).slice(0, USAGE_RECENT_LIMIT)
          }, [sessions])

          function detailFor(dayKey) {
            if (!dayKey) return null
            var bucket = aggregated.byDay[dayKey]
            if (bucket === undefined) return { key: dayKey, bucket: emptyBucket() }
            return { key: dayKey, bucket: bucket }
          }

          function renderStatCard(card) {
            var bucket = card.bucket || emptyBucket()
            var note = card.note
              ? ' · ' + card.note
              : bucket.sessions ? ' · ' + bucket.sessions + ' 个活跃日' : ''
            return h('div', { className: 'dt-card-tight', key: card.label },
              h('div', { className: 'dt-stat-label' }, card.label),
              h('div', { className: 'dt-stat-value' }, formatTokens(bucket.t)),
              h('div', { className: 'dt-stat-sub' },
                formatCount(bucket.prompts) + ' 次提问 · ' + formatCount(bucket.steps) + ' 步' + note)
            )
          }

          function renderHeat() {
            var monthRow = []
            var previousMonth = -1
            for (var week = 0; week < heat.columns.length; week++) {
              var month = heat.columns[week][0].day.getMonth()
              monthRow.push(h('div', { className: 'dt-heat-month', key: 'm' + week }, month === previousMonth ? '' : (month + 1) + '月'))
              previousMonth = month
            }
            var cells = []
            for (var columnIndex = 0; columnIndex < heat.columns.length; columnIndex++) {
              for (var rowIndex = 0; rowIndex < 7; rowIndex++) {
                var cell = heat.columns[columnIndex][rowIndex]
                cells.push(h('div', {
                  key: cell.key,
                  className: 'dt-heat-cell',
                  'data-level': String(cell.level),
                  'data-future': cell.future ? 'true' : 'false',
                  'data-active': hoverKey === cell.key ? 'true' : 'false',
                  title: cell.future ? '' : cellTooltip(cell),
                  onMouseEnter: function (key) { return function () { if (key) setHoverKey(key) } }(cell.future ? '' : cell.key),
                }))
              }
            }
            var detail = detailFor(hoverKey || todayKey)
            var bucket = detail ? detail.bucket : emptyBucket()
            return h('div', { className: 'dt-card' },
              h('div', { className: 'dt-actions', style: { justifyContent: 'space-between' } },
                h('div', { className: 'dt-section', style: { marginBottom: 0 } }, '每日 Token 用量'),
                h('div', { className: 'dt-tabs' },
                  [13, 26, 52].map(function (option) {
                    return h('button', {
                      key: option,
                      className: 'dt-tab',
                      'data-active': weeks === option ? 'true' : 'false',
                      onClick: function () { setWeeks(option) },
                    }, '近 ' + option + ' 周')
                  })
                )
              ),
              h('div', { style: { marginTop: 12 } },
                h('div', { className: 'dt-heat' },
                  h('div', { className: 'dt-heat-months', style: { gridTemplateColumns: 'repeat(' + heat.columns.length + ', var(--dt-cell))' } }, monthRow),
                  h('div', { className: 'dt-heat-body' },
                    h('div', { className: 'dt-heat-weekdays' }, WEEKDAY_SHORT.map(function (label, index) {
                      return h('div', { className: 'dt-heat-weekday', key: index }, label)
                    })),
                    h('div', { className: 'dt-heat-grid' }, cells)
                  )
                )
              ),
              h('div', { className: 'dt-heat-foot', style: { marginTop: 10 } },
                h('div', { className: 'dt-heat-detail' },
                  detail
                    ? h('span', null,
                        h('b', null, hoverKey ? formatDayLabel(detail.key) : '今天'),
                        ' · ',
                        h('b', null, formatTokens(bucket.t) + ' Token'),
                        ' · ' + formatCount(bucket.prompts) + ' 次提问 · ' + formatCount(bucket.steps) + ' 步',
                        bucket.sessions ? ' · ' + bucket.sessions + ' 个会话' : '',
                        bucket.t > 0
                          ? h('span', null, ' · 输入 ' + formatCount(bucket.i) + ' · 输出 ' + formatCount(bucket.o) + ' · 缓存读 ' + formatCount(bucket.cr))
                          : null
                      )
                    : h('span', null, '暂无数据')
                ),
                h('div', { className: 'dt-heat-legend' },
                  h('span', null, '少'),
                  [0, 1, 2, 3, 4].map(function (level) {
                    return h('span', { className: 'dt-heat-cell', key: level, 'data-level': String(level) })
                  }),
                  h('span', null, '多')
                )
              )
            )
          }

          function cellTooltip(cell) {
            if (cell.future) return ''
            if (!cell.bucket) return formatDayLabel(cell.key) + ' · 无用量'
            return formatDayLabel(cell.key) + ' · ' + formatTokens(cell.tokens) + ' Token · '
              + formatCount(cell.bucket.prompts) + ' 次提问 · ' + formatCount(cell.bucket.steps) + ' 步'
              + (cell.bucket.sessions ? ' · ' + cell.bucket.sessions + ' 个会话' : '')
          }

          function renderRecent() {
            if (recent.length === 0) return h('div', { className: 'dt-hint' }, '暂无可统计的会话。')
            return h('div', null, recent.map(function (session) {
              var totals = session.totals || emptyBucket()
              return h('div', { className: 'dt-session-row', key: session.id },
                h('div', { className: 'dt-grow' },
                  h('div', { className: 'dt-name' }, session.title || session.id),
                  h('div', { className: 'dt-sub' }, (session.cwd || '') + (session.truncated ? ' · 日志过长,仅统计了前段' : ''))
                ),
                h('div', { className: 'dt-session-meta' }, formatDateTime(session.updatedAt || Date.now())),
                h('div', { className: 'dt-session-tokens' }, formatTokens(totals.t))
              )
            }))
          }

          var scanning = status === 'loading'
          var statusText = scanning && progress.total > 0
            ? '正在扫描 ' + progress.done + '/' + progress.total + ' 个会话…'
            : scanning ? '正在读取会话列表…' : ''

          return h('div', { className: 'dt-root', style: { maxHeight: '100%', overflow: 'auto' } },
            h('div', null,
              h('div', { className: 'dt-title' }, '使用统计'),
              h('div', { className: 'dt-desc' }, '按天统计本机 Harness 会话的模型用量:数据取自本地会话日志,在页面内汇总,不上传。')
            ),
            h('div', { className: 'dt-usage-toolbar' },
              h('button', {
                className: 'dt-btn',
                'data-kind': 'primary',
                disabled: scanning,
                onClick: function () { setAttempt(function (value) { return value + 1 }) },
              }, scanning ? '扫描中…' : '重新扫描'),
              statusText ? h('span', { className: 'dt-usage-status' }, statusText) : null,
              !scanning && report
                ? h('span', { className: 'dt-usage-status' },
                    '共 ' + report.sessions.length + ' 个会话 · ' + formatDateTime(report.scannedAt) + ' 更新')
                : null
            ),
            error ? h('div', { className: 'dt-notice', 'data-kind': 'error' }, error) : null,
            report && report.failed > 0
              ? h('div', { className: 'dt-notice', 'data-kind': 'warn' },
                  report.failed + ' 个会话读取失败' + (report.failureMessage ? ':' + report.failureMessage : ''))
              : null,
            h('div', { className: 'dt-cards' }, cards.map(renderStatCard)),
            renderHeat(),
            h('div', { className: 'dt-card' },
              h('div', { className: 'dt-section' }, '用量最多的会话'),
              renderRecent()
            ),
            h('div', { className: 'dt-footnote' },
              '口径:每条 assistant/message 上报的 token 用量(输入 + 输出 + 缓存读 + 缓存写),按事件落盘时间归入本地自然日;'
              + '同一 (turn, step) 的重复上报只计最后一次,重试消耗照常累计。'
              + (report && (report.skipped > 0 || report.truncated > 0)
                ? '(跳过 ' + report.skipped + ' 个委派会话,截断 ' + report.truncated + ' 个超长日志)'
                : '')
            )
          )
        }
      }

      // ===================== 插件定义 =====================
      function apply(ctx) {
        ensureStyles()
        var scanner = createUsageScanner(ctx)
        var UsageSection = createUsageSection(scanner)
        ctx.slots.inject('settings.section', function () {
          return ctx.slots.register({
            name: 'settings.section',
            id: 'usage',
            order: 30,
            label: '使用统计',
          }, UsageSection)
        })
        ctx.slots.inject('settings.section', function () {
          return ctx.slots.register({
            name: 'settings.section',
            id: 'desktop-tools',
            order: 90,
            label: '桌面工具',
          }, DesktopToolsSection)
        })
      }

      // inject 只声明两个设置页必需的服务:slots。会话接口按可选服务用 ctx.get 读取,
      // 避免命名空间缺失时整个插件(含「桌面工具」页)被挂起。
      return { name: 'dsh-desktop-tools', inject: ['slots'], apply: apply }
    },
  })
})()
