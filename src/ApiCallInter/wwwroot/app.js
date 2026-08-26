/* ApiCallInter Web 管理页（Vue 3 自托管，无构建链）
 * 后端契约（camelCase JSON）：
 *  /api/projects GET/POST/PUT/DELETE；/api/projects/{id}/endpoints POST；/api/endpoints/{id} PUT/DELETE
 *  /api/endpoints/{id}/invoke POST → {success, statusCode, elapsedMs, errorMessage}
 *  /api/logs?projectId=&endpointId=&result=&page=&pageSize= → {items,total,page,pageSize}
 *  /api/overview → {version, uptime(TimeSpan 字符串), processStartTime, workingSetBytes, managedMemoryBytes, stats24h:{total,failed}, projects:[...]}
 *  /api/settings GET；PUT {webPort} → {needsRestart}；/api/settings/autostart POST {enabled}；/api/app/restart POST
 *  /api/update/check|prepare|restart（Task 10，未上线时 404 → 关于页显示"更新服务暂不可用"）
 */

// fetch 封装：空响应体、非 JSON 错误体（404 空 body）都要兜住；400 错误体 {message} 透出后端校验文案
const api = {
  async request(method, url, body) {
    const opt = { method };
    if (body !== undefined) {
      opt.headers = { 'Content-Type': 'application/json' };
      opt.body = JSON.stringify(body);
    }
    const res = await fetch(url, opt);
    const text = await res.text();
    let data = null;
    if (text) { try { data = JSON.parse(text); } catch (e) { data = null; } }
    if (!res.ok) throw new Error((data && data.message) ? data.message : ('HTTP ' + res.status));
    return data;
  },
  get: (u) => api.request('GET', u),
  post: (u, body) => api.request('POST', u, body ?? {}),
  put: (u, body) => api.request('PUT', u, body),
  del: (u) => api.request('DELETE', u),
};

/* ======================= 概览页 ======================= */

const OverviewPage = {
  props: ['overview'],
  emits: ['refresh', 'invoke', 'toggle'],
  template: `
  <div>
    <div class="stat-row">
      <div class="card stat"><div class="num">{{ overview.projects?.length ?? '…' }}</div><div class="label">项目</div></div>
      <div class="card stat"><div class="num">{{ enabledCount }}</div><div class="label">启用中</div></div>
      <div class="card stat"><div class="num">{{ overview.stats24h?.total ?? '…' }}</div><div class="label">24h 请求</div></div>
      <div class="card stat"><div class="num bad">{{ overview.stats24h?.failed ?? '…' }}</div><div class="label">24h 失败</div></div>
      <div class="card stat"><div class="num">{{ rate24h }}</div><div class="label">24h 成功率</div></div>
    </div>
    <div class="grid">
      <div class="card proj-card" v-for="p in overview.projects" :key="p.id">
        <h3>{{ p.name }} <span :class="p.enabled ? 'badge badge-green' : 'badge badge-gray'">{{ p.enabled ? '启用' : '停用' }}</span></h3>
        <div class="meta">间隔 {{ p.intervalSeconds }}s ± {{ p.jitterMilliseconds }}ms · {{ p.enabledEndpointCount }}/{{ p.endpointCount }} 接口</div>
        <div class="next-run"><span>下次执行</span><b class="mono">{{ p.nextRunAt ? countdown(p.nextRunAt) : '—' }}</b></div>
        <div class="meta" v-if="p.lastRound">最近一轮 {{ p.lastRound.ok }}/{{ p.lastRound.total }} 成功<span v-if="p.lastRound.failed" class="badge badge-red">{{ p.lastRound.failed }} 失败</span></div>
        <div class="ops">
          <button class="btn" @click="$emit('invoke', p)">立即请求</button>
          <button class="btn" @click="$emit('toggle', p)">{{ p.enabled ? '停用' : '启用' }}</button>
        </div>
      </div>
    </div>
  </div>`,
  computed: {
    enabledCount() { return (this.overview.projects || []).filter(p => p.enabled).length; },
    rate24h() {
      const s = this.overview.stats24h;
      return !s || !s.total ? '—' : (100 * (s.total - s.failed) / s.total).toFixed(1) + '%';
    }
  },
  methods: {
    countdown(next) {
      const d = new Date(next) - Date.now();
      if (d <= 0) return '即将执行';
      const h = Math.floor(d / 3600000), m = Math.floor(d % 3600000 / 60000), s = Math.floor(d % 60000 / 1000);
      const mm = String(m).padStart(2, '0'), ss = String(s).padStart(2, '0');
      return h ? h + ':' + mm + ':' + ss : mm + ':' + ss;
    }
  }
};

/* ======================= 项目管理页 ======================= */

const ProjectsPage = {
  emits: ['changed'],
  data: () => ({
    projects: [],          // /api/projects 全量实体（含 endpoints）
    lastRoundById: {},     // /api/overview 的项目最近一轮，按 id 关联
    search: '',
    expanded: {},          // projectId -> 是否展开接口子表
    epLatest: {},          // endpointId -> 最新一条日志（展开时懒加载 /api/logs）
    editing: null,         // 项目表单（null=关闭弹层）
    epEditing: null,       // 接口表单
    dragId: null,          // 拖拽中的项目 id（null=未拖拽）
    dragOverId: null       // 拖拽悬停目标行 id（高亮用）
  }),
  computed: {
    filteredProjects() {
      const k = (this.search || '').trim().toLowerCase();
      if (!k) return this.projects;
      return this.projects.filter(p => (p.name + ' ' + (p.description || '')).toLowerCase().includes(k));
    }
  },
  async mounted() { await this.load(); },
  methods: {
    async load() {
      try {
        const [list, ov] = await Promise.all([api.get('/api/projects'), api.get('/api/overview')]);
        this.projects = list || [];
        const map = {};
        ((ov && ov.projects) || []).forEach(p => { map[p.id] = p.lastRound; });
        this.lastRoundById = map;
      } catch (e) { alert('加载失败：' + e.message); }
    },
    roundBadge(p) {
      const lr = this.lastRoundById[p.id];
      if (!lr) return { cls: 'badge badge-gray', text: '暂无' };
      return lr.failed ? { cls: 'badge badge-red', text: lr.ok + '/' + lr.total + ' 成功' }
                       : { cls: 'badge badge-green', text: lr.ok + '/' + lr.total + ' 成功' };
    },
    // 拖拽排序：搜索过滤时子序列与全量序不对应，禁用（把手不渲染）
    dragStart(p, ev) {
      this.dragId = p.id;
      ev.dataTransfer.effectAllowed = 'move';
      ev.dataTransfer.setData('text/plain', String(p.id));   // Firefox 需 setData 才能启动拖拽
    },
    dragOver(p, ev) {
      if (this.dragId === null || this.dragId === p.id) return;
      ev.preventDefault();   // 允许放置，否则不触发 drop
      ev.dataTransfer.dropEffect = 'move';
      this.dragOverId = p.id;
    },
    dragEnd() { this.dragId = null; this.dragOverId = null; },
    async drop(p, ev) {
      ev.preventDefault();
      const dragId = this.dragId;
      this.dragId = null; this.dragOverId = null;
      if (dragId === null || dragId === p.id) return;
      const ids = this.projects.map(x => x.id);
      ids.splice(ids.indexOf(dragId), 1);
      // 鼠标在目标行上/下半决定前插/后插
      const rect = ev.currentTarget.getBoundingClientRect();
      const after = ev.clientY > rect.top + rect.height / 2;
      ids.splice(ids.indexOf(p.id) + (after ? 1 : 0), 0, dragId);
      try {
        await api.put('/api/projects/order', { ids });
        await this.load(); this.$emit('changed');   // changed → 根组件刷新概览与项目缓存
      } catch (e) { alert('排序失败：' + e.message); await this.load(); }   // 失败重载回滚显示
    },
    async toggleExpand(p) {
      this.expanded[p.id] = !this.expanded[p.id];
      if (this.expanded[p.id]) await this.loadLatest(p.id);
    },
    // 子表"最近"列：取该项目最近 100 条日志，每个 endpoint 取首条（接口已按 Id 倒序）
    async loadLatest(projectId) {
      try {
        const r = await api.get('/api/logs?projectId=' + projectId + '&pageSize=100');
        const map = {};
        ((r && r.items) || []).forEach(l => { if (!(l.endpointId in map)) map[l.endpointId] = l; });
        for (const k in map) this.epLatest[k] = map[k];
      } catch (e) { /* 子表"最近"列退化为 —，不阻断页面 */ }
    },
    epLatestText(e) {
      const l = this.epLatest[e.id];
      if (!l) return '—';
      return (l.statusCode ?? 'ERR') + ' · ' + l.elapsedMs + 'ms';
    },
    openCreate() {
      this.editing = { id: 0, name: '', description: '', intervalSeconds: 300, jitterMilliseconds: 3000, enabled: true };
    },
    openEdit(p) { this.editing = { ...p }; },
    async saveProject() {
      const f = this.editing;
      if (!f) return;
      const payload = {
        name: (f.name || '').trim(), description: f.description || '',
        intervalSeconds: Number(f.intervalSeconds), jitterMilliseconds: Number(f.jitterMilliseconds), enabled: !!f.enabled
      };
      try {
        if (f.id) await api.put('/api/projects/' + f.id, payload);
        else await api.post('/api/projects', payload);
        this.editing = null; await this.load(); this.$emit('changed');
      } catch (e) { alert('保存失败：' + e.message); }
    },
    async removeProject(p) {
      if (!confirm('确定删除项目「' + p.name + '」？其下全部接口与请求日志将一并删除。')) return;
      try { await api.del('/api/projects/' + p.id); await this.load(); this.$emit('changed'); }
      catch (e) { alert('删除失败：' + e.message); }
    },
    async toggleProject(p) {
      try {
        // PUT 为整体覆盖（description 缺省会清空），必须带全字段
        await api.put('/api/projects/' + p.id, {
          name: p.name, description: p.description,
          intervalSeconds: p.intervalSeconds, jitterMilliseconds: p.jitterMilliseconds, enabled: !p.enabled
        });
        await this.load(); this.$emit('changed');
      } catch (e) { alert('操作失败：' + e.message); }
    },
    openEpCreate(p) {
      this.epEditing = { id: 0, projectId: p.id, name: '', url: '', method: 'GET', headers: '', body: '', timeoutSeconds: 30, enabled: true };
    },
    openEpEdit(e) { this.epEditing = { ...e }; },
    async saveEndpoint() {
      const f = this.epEditing;
      if (!f) return;
      const payload = {
        name: (f.name || '').trim(), url: (f.url || '').trim(), method: f.method,
        headers: f.headers || '', body: f.body || '',
        timeoutSeconds: Number(f.timeoutSeconds), enabled: !!f.enabled
      };
      try {
        if (f.id) await api.put('/api/endpoints/' + f.id, payload);
        else await api.post('/api/projects/' + f.projectId + '/endpoints', payload);
        this.epEditing = null; await this.load(); this.$emit('changed');
        if (this.expanded[f.projectId]) await this.loadLatest(f.projectId);
      } catch (e) { alert('保存失败：' + e.message); }
    },
    async removeEndpoint(e) {
      if (!confirm('确定删除接口「' + e.name + '」？其请求日志将一并删除。')) return;
      try {
        await api.del('/api/endpoints/' + e.id);
        await this.load(); this.$emit('changed');
        if (this.expanded[e.projectId]) await this.loadLatest(e.projectId);
      } catch (err) { alert('删除失败：' + err.message); }
    },
    async toggleEndpoint(e) {
      try {
        await api.put('/api/endpoints/' + e.id, {
          name: e.name, url: e.url, method: e.method, headers: e.headers || '', body: e.body || '',
          timeoutSeconds: e.timeoutSeconds, enabled: !e.enabled
        });
        await this.load(); this.$emit('changed');
      } catch (err) { alert('操作失败：' + err.message); }
    },
    async invokeEndpoint(e) {
      try {
        const r = await api.post('/api/endpoints/' + e.id + '/invoke');
        if (r && r.success) alert('「' + e.name + '」请求成功：HTTP ' + (r.statusCode ?? 200) + ' · ' + r.elapsedMs + 'ms');
        else alert('「' + e.name + '」请求失败：' + (r && r.statusCode ? 'HTTP ' + r.statusCode + ' · ' : '') + ((r && r.errorMessage) || '未知错误') + ' · ' + (r ? r.elapsedMs : '?') + 'ms');
      } catch (err) { alert('「' + e.name + '」请求失败：' + err.message); }
    }
  },
  template: `
  <div>
    <div class="toolbar">
      <button class="btn btn-primary" @click="openCreate">＋ 新建项目</button>
      <input type="text" v-model="search" placeholder="搜索项目名 / 备注" style="margin-left:auto;width:240px">
    </div>
    <div class="card" style="padding:0;overflow:hidden">
      <table>
        <tr><th style="width:28px"></th><th>项目</th><th>备注</th><th>间隔</th><th>抖动</th><th>接口</th><th>最近状态</th><th>启用</th><th style="width:200px">操作</th></tr>
        <template v-for="p in filteredProjects" :key="p.id">
          <tr :class="{ dragging: dragId === p.id, 'drag-over': dragOverId === p.id }"
              @dragover="dragOver(p, $event)" @drop="drop(p, $event)">
            <td><span v-if="!search" class="drag-handle" draggable="true" title="拖动调整顺序"
                  @dragstart="dragStart(p, $event)" @dragend="dragEnd">⠿</span></td>
            <td><b>{{ p.name }}</b></td>
            <td>{{ p.description }}</td>
            <td class="mono">{{ p.intervalSeconds }}s</td>
            <td class="mono">±{{ p.jitterMilliseconds }}ms</td>
            <td>{{ (p.endpoints || []).length }}</td>
            <td><span :class="roundBadge(p).cls">{{ roundBadge(p).text }}</span></td>
            <td><div class="switch" :class="{on: p.enabled}" @click="toggleProject(p)"></div></td>
            <td>
              <button class="btn-link" @click="toggleExpand(p)">{{ expanded[p.id] ? '收起接口' : '展开接口' }}</button>
              <button class="btn-link" @click="openEdit(p)">编辑</button>
              <button class="btn-link" style="color:#dc2626" @click="removeProject(p)">删除</button>
            </td>
          </tr>
          <tr v-if="expanded[p.id]" class="expanded-row">
            <td colspan="9" style="padding:14px 16px">
              <div style="display:flex;justify-content:space-between;margin-bottom:8px">
                <b>{{ p.name }} · 接口列表</b>
                <button class="btn" @click="openEpCreate(p)">＋ 新建接口</button>
              </div>
              <table>
                <tr><th>接口名</th><th>URL</th><th>方法</th><th>超时</th><th>最近</th><th>启用</th><th style="width:190px">操作</th></tr>
                <tr v-for="e in p.endpoints" :key="e.id">
                  <td>{{ e.name }}</td>
                  <td class="mono">{{ e.url }}</td>
                  <td><span class="badge" :class="e.method === 'GET' ? 'badge-green' : 'badge-purple'">{{ e.method }}</span></td>
                  <td class="mono">{{ e.timeoutSeconds }}s</td>
                  <td><span class="mono" :class="epLatest[e.id] && epLatest[e.id].success ? 'code200' : 'codeERR'">{{ epLatestText(e) }}</span></td>
                  <td><div class="switch" :class="{on: e.enabled}" @click="toggleEndpoint(e)"></div></td>
                  <td>
                    <button class="btn-link" @click="invokeEndpoint(e)">立即请求</button>
                    <button class="btn-link" @click="openEpEdit(e)">编辑</button>
                    <button class="btn-link" style="color:#dc2626" @click="removeEndpoint(e)">删除</button>
                  </td>
                </tr>
                <tr v-if="!(p.endpoints || []).length"><td colspan="7" style="color:#9ca3af">暂无接口，点右上"＋ 新建接口"添加</td></tr>
              </table>
            </td>
          </tr>
        </template>
        <tr v-if="!filteredProjects.length"><td colspan="9" style="color:#9ca3af">暂无项目，点"＋ 新建项目"创建</td></tr>
      </table>
    </div>

    <div class="modal-mask" v-if="editing" @click="editing=null"></div>
    <div class="modal" v-if="editing">
      <h3>{{ editing.id ? '编辑项目' : '新建项目' }}</h3>
      <div class="form-row"><label>项目名</label><input type="text" v-model="editing.name" placeholder="如 SMOM-华东"></div>
      <div class="form-row"><label>备注</label><input type="text" v-model="editing.description" placeholder="如 客户A IPSec VPN"></div>
      <div class="form-row"><label>间隔（秒）</label><input type="number" v-model.number="editing.intervalSeconds" min="30"><span class="hint">≥ 30</span></div>
      <div class="form-row"><label>抖动（毫秒）</label><input type="number" v-model.number="editing.jitterMilliseconds" min="0"><span class="hint">≥ 0 且小于间隔×1000</span></div>
      <div class="form-row"><label>启用</label><div class="switch" :class="{on: editing.enabled}" @click="editing.enabled = !editing.enabled"></div></div>
      <div class="form-actions">
        <button class="btn" @click="editing=null">取消</button>
        <button class="btn btn-primary" @click="saveProject">保存</button>
      </div>
    </div>

    <div class="modal-mask" v-if="epEditing" @click="epEditing=null"></div>
    <div class="modal" v-if="epEditing">
      <h3>{{ epEditing.id ? '编辑接口' : '新建接口' }}</h3>
      <div class="form-row"><label>接口名</label><input type="text" v-model="epEditing.name" placeholder="如 健康检查"></div>
      <div class="form-row"><label>URL</label><input type="text" v-model="epEditing.url" placeholder="https://example.com/api/health" style="width:100%"></div>
      <div class="form-row"><label>方法</label>
        <select v-model="epEditing.method">
          <option>GET</option><option>POST</option><option>PUT</option><option>HEAD</option>
        </select>
        <span class="hint">仅支持 GET/POST/PUT/HEAD</span>
      </div>
      <div class="form-row" style="align-items:flex-start"><label>请求头 JSON</label><textarea v-model="epEditing.headers" placeholder='{"Authorization":"Bearer …"}' style="flex:1"></textarea></div>
      <div class="form-row" style="align-items:flex-start"><label>请求体</label><textarea v-model="epEditing.body" placeholder="POST/PUT 的 body（可空）" style="flex:1"></textarea></div>
      <div class="form-row"><label>超时（秒）</label><input type="number" v-model.number="epEditing.timeoutSeconds" min="1" max="120"><span class="hint">1~120</span></div>
      <div class="form-row"><label>启用</label><div class="switch" :class="{on: epEditing.enabled}" @click="epEditing.enabled = !epEditing.enabled"></div></div>
      <div class="form-actions">
        <button class="btn" @click="epEditing=null">取消</button>
        <button class="btn btn-primary" @click="saveEndpoint">保存</button>
      </div>
    </div>
  </div>`
};

/* ======================= 请求日志页 ======================= */

const LogsPage = {
  data: () => ({
    items: [], total: 0, page: 1, pageSize: 50,
    projectId: '', endpointId: '', result: '',
    projects: [],   // 筛选下拉 + 项目/接口名内存关联（日志项不含名称）
    loading: false
  }),
  computed: {
    totalPages() { return Math.max(1, Math.ceil(this.total / this.pageSize)); },
    endpointOptions() {
      if (this.projectId) {
        const p = this.projects.find(x => x.id === Number(this.projectId));
        return p ? (p.endpoints || []) : [];
      }
      return this.projects.flatMap(p => p.endpoints || []);
    },
    projName() { const m = {}; this.projects.forEach(p => { m[p.id] = p.name; }); return m; },
    epName() { const m = {}; this.projects.forEach(p => (p.endpoints || []).forEach(e => { m[e.id] = e.name; })); return m; }
  },
  async mounted() {
    try { this.projects = (await api.get('/api/projects')) || []; } catch (e) { this.projects = []; }
    await this.query(true);
  },
  methods: {
    async query(reset) {
      if (reset) this.page = 1;
      const qs = new URLSearchParams({ page: String(this.page), pageSize: String(this.pageSize) });
      if (this.projectId) qs.set('projectId', this.projectId);
      if (this.endpointId) qs.set('endpointId', this.endpointId);
      if (this.result) qs.set('result', this.result);
      try {
        this.loading = true;
        const r = await api.get('/api/logs?' + qs.toString());
        this.items = (r && r.items) || [];
        this.total = (r && r.total) || 0;
        this.page = r.page || this.page;
        this.pageSize = r.pageSize || this.pageSize;
      } catch (e) { alert('查询失败：' + e.message); }
      finally { this.loading = false; }
    },
    onProjectChange() { this.endpointId = ''; },
    goPage(n) { if (n >= 1 && n <= this.totalPages && n !== this.page) { this.page = n; this.query(false); } },
    fmtTime(t) {
      const d = new Date(t);
      if (isNaN(d)) return String(t);
      const p = n => String(n).padStart(2, '0');
      return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' + p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds());
    }
  },
  template: `
  <div>
    <div class="toolbar">
      <select v-model="projectId" @change="onProjectChange">
        <option value="">全部项目</option>
        <option v-for="p in projects" :key="p.id" :value="String(p.id)">{{ p.name }}</option>
      </select>
      <select v-model="endpointId">
        <option value="">全部接口</option>
        <option v-for="e in endpointOptions" :key="e.id" :value="String(e.id)">{{ e.name }}</option>
      </select>
      <select v-model="result">
        <option value="">全部结果</option>
        <option value="success">成功</option>
        <option value="failed">失败</option>
      </select>
      <button class="btn btn-primary" @click="query(true)">查询</button>
      <span style="margin-left:auto;color:#6b7280">共 {{ total }} 条</span>
    </div>
    <div class="card" style="padding:0;overflow:hidden">
      <table>
        <tr><th>时间</th><th>项目</th><th>接口</th><th>状态</th><th>耗时</th><th>错误信息</th></tr>
        <tr v-for="l in items" :key="l.id">
          <td class="mono">{{ fmtTime(l.requestedAt) }}</td>
          <td>{{ projName[l.projectId] ?? ('#' + l.projectId) }}</td>
          <td>{{ epName[l.endpointId] ?? ('#' + l.endpointId) }}</td>
          <td :class="l.success ? 'code200 mono' : 'codeERR mono'">{{ l.statusCode ?? 'ERR' }}</td>
          <td class="mono">{{ l.elapsedMs }}ms</td>
          <td class="err">{{ l.errorMessage }}</td>
        </tr>
        <tr v-if="!items.length"><td colspan="6" style="color:#9ca3af">暂无日志</td></tr>
      </table>
      <div class="pager">
        <button class="btn" :disabled="page <= 1" @click="goPage(page - 1)">‹ 上一页</button>
        <span>第 {{ page }} / {{ totalPages }} 页</span>
        <button class="btn" :disabled="page >= totalPages" @click="goPage(page + 1)">下一页 ›</button>
      </div>
    </div>
  </div>`
};

/* ======================= 系统设置页 ======================= */

const SettingsPage = {
  props: ['settings'],
  emits: ['saved', 'restarting'],
  data: () => ({ webPort: 0, autoStart: false, needsRestart: false, busy: false }),
  watch: {
    settings: {
      immediate: true,
      handler(s) {
        if (s && s.webPort) { this.webPort = s.webPort; this.autoStart = !!s.autoStart; this.needsRestart = false; }
      }
    }
  },
  methods: {
    async save() {
      const port = Number(this.webPort);
      if (!Number.isInteger(port) || port < 1024 || port > 65535) { alert('端口必须在 1024~65535'); return; }
      try {
        this.busy = true;
        const r = await api.put('/api/settings', { webPort: port });
        this.needsRestart = !!(r && r.needsRestart);
        alert(this.needsRestart ? '端口已保存，需重启程序后生效' : '端口未变化，无需重启');
        this.$emit('saved');
      } catch (e) { alert('保存失败：' + e.message); }
      finally { this.busy = false; }
    },
    async toggleAutoStart() {
      try {
        await api.post('/api/settings/autostart', { enabled: !this.autoStart });
        this.autoStart = !this.autoStart;
        this.$emit('saved');
      } catch (e) { alert('设置失败：' + e.message); }
    },
    async restart() {
      if (!confirm('确定立即重启程序？重启期间托盘图标会短暂消失，定时调度暂停数秒。')) return;
      try { await api.post('/api/app/restart'); this.$emit('restarting'); }
      catch (e) { alert('重启失败：' + e.message); }
    }
  },
  template: `
  <div class="card" style="width:100%">
    <div class="sec-h">系统设置</div>
    <div class="form-row">
      <label>监控端口</label>
      <input type="number" class="mono" v-model.number="webPort" min="1024" max="65535">
      <span class="hint">Web 管理页监听端口（1024–65535），保存后需重启程序生效</span>
    </div>
    <div class="form-row">
      <label>开机自启</label>
      <div class="switch" :class="{on: autoStart}" @click="toggleAutoStart"></div>
      <span class="hint">写入当前用户注册表 Run 键，与托盘菜单开关同源</span>
    </div>
    <div class="form-row" style="margin-top:20px">
      <label></label>
      <button class="btn btn-primary" :disabled="busy" @click="save">保存</button>
      <button class="btn" @click="restart">立即重启程序</button>
    </div>
    <div class="note" v-if="needsRestart">端口已修改但尚未生效：点"立即重启程序"后生效。重启后请用新端口访问管理页。</div>
    <div class="note">提示：修改端口后需重启程序；重启期间托盘图标会短暂消失，定时调度暂停数秒。</div>
  </div>`
};

/* ======================= 关于页（更新服务 Task 10 未上线时降级） ======================= */

const AboutPage = {
  props: ['version'],
  emits: ['restarting'],
  data: () => ({ check: null, unavailable: false, busy: false }),
  computed: {
    latestVersion() { return this.check ? (this.check.latestVersion ?? this.check.version ?? '—') : '—'; },
    hasUpdate() { return !!(this.check && this.check.hasUpdate); },
    notesText() {
      if (!this.check) return '';
      const n = this.check.notes ?? this.check.releaseNotes ?? this.check.updateNotes;
      if (Array.isArray(n)) return n.join('\n');
      return n ? String(n) : '';
    }
  },
  methods: {
    async doCheck() {
      this.busy = true; this.unavailable = false; this.check = null;
      try { this.check = await api.get('/api/update/check'); }
      catch (e) { this.unavailable = true; }   // 404（Task 10 未上线）/ 其他失败 → 暂不可用
      finally { this.busy = false; }
    },
    async doUpgrade() {
      if (!confirm('确定一键升级？将下载新版本并自动重启程序完成替换。')) return;
      try {
        this.busy = true;
        await api.post('/api/update/prepare');
        await api.post('/api/update/restart');
        alert('正在重启升级…');
        this.$emit('restarting');
      } catch (e) { this.unavailable = true; }
      finally { this.busy = false; }
    }
  },
  template: `
  <div class="card" style="width:100%">
    <div class="sec-h">关于</div>
    <div class="form-row">
      <label>当前版本</label><b class="mono">v{{ version || '…' }}</b>
      <button class="btn" style="margin-left:12px" :disabled="busy" @click="doCheck">检查更新</button>
    </div>
    <template v-if="unavailable">
      <div class="note">更新服务暂不可用（检查更新接口未上线或无法访问），当前版本可正常使用。</div>
    </template>
    <template v-else-if="check">
      <hr style="border:none;border-top:1px solid #f3f4f6;margin:14px 0">
      <div class="form-row">
        <label>最新版本</label>
        <b class="mono" :class="hasUpdate ? 'ok' : ''">v{{ latestVersion }}</b>
        <span v-if="hasUpdate" class="badge badge-green">有新版本</span>
        <span v-else class="badge badge-gray">已是最新</span>
      </div>
      <div class="form-row" v-if="notesText" style="align-items:flex-start">
        <label>更新说明</label>
        <div style="color:#374151;white-space:pre-line">{{ notesText }}</div>
      </div>
      <div class="form-row" style="margin-top:20px" v-if="hasUpdate">
        <label></label>
        <button class="btn btn-primary" :disabled="busy" @click="doUpgrade">一键升级（下载 → 重启）</button>
      </div>
    </template>
    <div class="note">升级流程：下载新版本 zip → 解压校验 → 自动重启完成替换（全过程写 update.log，失败不影响当前版本运行）。</div>
  </div>`
};

/* ======================= 根实例 ======================= */

const { createApp } = Vue;
createApp({
  components: { OverviewPage, ProjectsPage, LogsPage, SettingsPage, AboutPage },
  data: () => ({
    tab: 'overview',
    tabs: [
      { id: 'overview', name: '概览' }, { id: 'projects', name: '项目管理' },
      { id: 'logs', name: '请求日志' }, { id: 'settings', name: '系统设置' }, { id: 'about', name: '关于' }
    ],
    overview: {},
    settings: {},
    projectsCache: [],   // 全量项目实体（含 endpoints），供项目级"立即请求"循环与启停切换取全字段
    timer: null
  }),
  computed: {
    // 后端 Uptime 是 TimeSpan 字符串（如 "1.02:03:04.5"），优先用 processStartTime 现算秒数，失败再解析该字符串
    uptimeText() {
      const o = this.overview || {};
      let sec = null;
      if (o.processStartTime) {
        const t = Date.parse(o.processStartTime);
        if (!isNaN(t)) sec = (Date.now() - t) / 1000;
      }
      if (sec === null && o.uptime) sec = this.parseTimeSpan(o.uptime);
      return sec === null ? '—' : this.fmtDuration(Math.max(0, Math.floor(sec)));
    }
  },
  async mounted() {
    await this.loadOverview();
    await this.loadSettings();
    await this.loadProjectsCache();
    this.timer = setInterval(() => { if (this.tab === 'overview') this.loadOverview(); }, 5000);
  },
  unmounted() { clearInterval(this.timer); },
  methods: {
    async loadOverview() {
      try { this.overview = await api.get('/api/overview'); }
      catch (e) { this.overview = { stats24h: {} }; }
    },
    async loadSettings() {
      try { this.settings = await api.get('/api/settings'); } catch (e) { /* 设置页自行提示 */ }
    },
    async loadProjectsCache() {
      try { this.projectsCache = (await api.get('/api/projects')) || []; }
      catch (e) { this.projectsCache = []; }
    },
    async loadAll() { await Promise.all([this.loadOverview(), this.loadProjectsCache()]); },
    async ensureProjectFull(id) {
      let p = this.projectsCache.find(x => x.id === id);
      if (!p) { await this.loadProjectsCache(); p = this.projectsCache.find(x => x.id === id); }
      return p;
    },
    // 项目级"立即请求"：后端只有单接口 invoke，前端顺序调该项目全部启用接口后汇总（无项目级端点）
    async invokeProject(p) {
      const full = await this.ensureProjectFull(p.id);
      const eps = ((full && full.endpoints) || []).filter(e => e.enabled);
      if (!eps.length) { alert('「' + p.name + '」没有启用的接口'); return; }
      let ok = 0, firstErr = '';
      for (const e of eps) {
        try {
          const r = await api.post('/api/endpoints/' + e.id + '/invoke');
          if (r && r.success) ok++;
          else if (!firstErr) firstErr = (e.name + '：' + ((r && r.errorMessage) || ('HTTP ' + (r && r.statusCode))));
        } catch (err) { if (!firstErr) firstErr = e.name + '：' + err.message; }
      }
      alert('「' + p.name + '」立即请求完成：' + ok + '/' + eps.length + ' 成功' + (firstErr ? '\n首个失败：' + firstErr : ''));
      await this.loadOverview();
    },
    async toggleProject(p) {
      try {
        const full = await this.ensureProjectFull(p.id);
        if (!full) { alert('项目不存在或已删除，请刷新页面'); return; }
        await api.put('/api/projects/' + p.id, {
          name: full.name, description: full.description,
          intervalSeconds: full.intervalSeconds, jitterMilliseconds: full.jitterMilliseconds,
          enabled: !full.enabled
        });
        await this.loadOverview();
        await this.loadProjectsCache();
      } catch (e) { alert('操作失败：' + e.message); }
    },
    notifyRestart() {
      const newPort = Number(this.settings && this.settings.webPort);
      const curPort = Number(location.port) || (location.protocol === 'https:' ? 443 : 80);
      if (newPort && newPort !== curPort) {
        alert('程序即将重启并切换到新端口 ' + newPort + '，页面将在数秒后自动跳转…');
        setTimeout(() => { location.href = location.protocol + '//' + location.hostname + ':' + newPort + '/'; }, 5000);
      } else {
        alert('程序即将重启，页面稍后自动恢复');
        setTimeout(() => location.reload(), 5000);
      }
    },
    // 解析 .NET TimeSpan "c" 格式："[-][d.]hh:mm:ss[.fffffff]" → 秒
    parseTimeSpan(s) {
      const m = /^-?((\d+)\.)?(\d+):(\d+):(\d+)/.exec(String(s));
      if (!m) return null;
      return ((+m[2] || 0) * 86400) + ((+m[3]) * 3600) + ((+m[4]) * 60) + (+m[5]);
    },
    fmtDuration(sec) {
      if (!sec && sec !== 0) return '—';
      const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
      return (d ? d + 'd ' : '') + h + 'h ' + m + 'm';
    },
    fmtBytes(b) { return b ? (b / 1024 / 1024).toFixed(1) + ' MB' : '—'; }
  }
}).mount('#app');
