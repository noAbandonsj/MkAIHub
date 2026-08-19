/**
 * DataClawHub 前台原型 · 交互逻辑与共享组件渲染
 * 组件化思想：导航栏、页脚、制品卡片、分页均通过 JS 统一渲染复用
 */

/* ==========================================================================
   SVG 图标集合（内联，避免外部 CDN 依赖）
   ========================================================================== */
const icons = {
  search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/></svg>',
  download: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/><path d="M12 15V3"/></svg>',
  heart: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/></svg>',
  comment: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>',
  bell: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/></svg>',
  globe: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M2 12h20"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/></svg>',
  file: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>',
  check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5"/></svg>',
  upload: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m17 8-5-5-5 5"/><path d="M12 3v12"/></svg>',
  chevronDown: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>',
  arrowRight: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg>',
  clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/></svg>',
  cpu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M15 2v2M9 2v2M15 20v2M9 20v2M2 15h2M2 9h2M20 15h2M20 9h2"/></svg>',
  coin: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M8 12h8M12 8v8"/></svg>',
  eye: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7z"/><circle cx="12" cy="12" r="3"/></svg>',
  shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/></svg>',
  folder: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2z"/></svg>',
  github: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.58.11.79-.25.79-.56 0-.27-.01-1.17-.02-2.12-3.2.7-3.88-1.36-3.88-1.36-.52-1.33-1.28-1.68-1.28-1.68-1.04-.71.08-.7.08-.7 1.15.08 1.76 1.18 1.76 1.18 1.03 1.76 2.7 1.25 3.35.96.1-.75.4-1.25.72-1.54-2.55-.29-5.24-1.28-5.24-5.68 0-1.26.45-2.28 1.18-3.09-.12-.29-.51-1.46.11-3.04 0 0 .96-.31 3.15 1.18a10.9 10.9 0 0 1 5.74 0c2.19-1.49 3.15-1.18 3.15-1.18.62 1.58.23 2.75.11 3.04.73.81 1.18 1.83 1.18 3.09 0 4.41-2.69 5.38-5.25 5.66.41.35.77 1.05.77 2.12 0 1.53-.01 2.76-.01 3.14 0 .31.21.67.8.56A11.52 11.52 0 0 0 23.5 12C23.5 5.65 18.35.5 12 .5z"/></svg>',
  wechat: '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M9.5 4C5.36 4 2 6.69 2 10c0 1.9 1.05 3.6 2.7 4.7l-.7 2.1 2.4-1.2c.7.2 1.4.3 2.1.3.1 0 .2 0 .3 0-.1-.4-.2-.8-.2-1.2 0-3.1 3-5.7 6.7-5.7.2 0 .4 0 .7.1C15.2 6.1 12.6 4 9.5 4zM6.5 8.5a1 1 0 1 1 0-2 1 1 0 0 1 0 2zm5 0a1 1 0 1 1 0-2 1 1 0 0 1 0 2z"/><path d="M22 14.5c0-2.5-2.4-4.5-5.3-4.5s-5.3 2-5.3 4.5 2.4 4.5 5.3 4.5c.5 0 1-.1 1.5-.2l2 .8-.5-1.6c1.4-.8 2.3-2.1 2.3-3.5zm-7-1a.9.9 0 1 1 0-1.8.9.9 0 0 1 0 1.8zm4 0a.9.9 0 1 1 0-1.8.9.9 0 0 1 0 1.8z"/></svg>'
};

/* ==========================================================================
   共享组件：导航栏
   ========================================================================== */

/**
 * 渲染顶部导航栏到 #navbar 容器
 * @param {string} activeKey - 当前激活菜单项的 key（如 'artifacts'）
 */
function renderNavbar(activeKey) {
  const container = document.getElementById('navbar');
  if (!container) return;

  const menuHtml = navItems
    .map((item) => {
      // 有 children 时渲染下拉子菜单
      const childrenHtml =
        item.children && item.children.length
          ? `<div class="navDropdown">${item.children
              .map(
                (c) =>
                  `<a class="navDropdownItem" href="${c.href}">${c.label}</a>`
              )
              .join('')}</div>`
          : '';
      return `
      <div class="navItemWrap">
        <a class="navItem ${item.key === activeKey ? 'active' : ''}" href="${item.href}">
          ${item.label}
          ${item.hasDropdown ? `<span class="navCaret">${icons.chevronDown}</span>` : ''}
        </a>
        ${childrenHtml}
      </div>`;
    })
    .join('');

  container.innerHTML = `
    <div class="container navbarInner">
      <a class="navLogo" href="index.html">
        <span class="navLogoMark">D</span>
        <span class="navLogoText">DataClaw<span>Hub</span></span>
      </a>
      <nav class="navMenu">${menuHtml}</nav>
      <div class="navRight">
        <button class="iconBtn" aria-label="通知">
          ${icons.bell}
          <span class="notifyDot"></span>
        </button>
        <button class="langSwitch">${icons.globe} ZH</button>
        <a class="btn btnPrimary" href="login.html">登录</a>
      </div>
    </div>`;
}

/* ==========================================================================
   共享组件：页脚
   ========================================================================== */

/**
 * 渲染页脚到 #footer 容器
 */
function renderFooter() {
  const container = document.getElementById('footer');
  if (!container) return;

  container.innerHTML = `
    <div class="container footerInner">
      <div class="footerTop">
        <div class="footerBrand">
          <div class="footerLogo">
            <span class="navLogoMark">D</span>
            <span class="navLogoText">DataClaw<span>Hub</span></span>
          </div>
          <p class="footerBrandDesc">面向物流行业的 AI 原生开源协作社区，开放基础平台、技术工具、数据资源、行业知识与场景应用。</p>
        </div>
        <div class="footerLinks">
          <div class="footerCol">
            <div class="footerColTitle">产品</div>
            <ul>
              <li><a href="artifacts.html">制品库</a></li>
              <li><a href="tasks.html">任务</a></li>
              <li><a href="featured.html">探索</a></li>
              <li><a href="competitions.html">竞赛</a></li>
            </ul>
          </div>
          <div class="footerCol">
            <div class="footerColTitle">资源</div>
            <ul>
              <li><a href="knowledge.html">知识库</a></li>
              <li><a href="issues.html">Issues</a></li>
            </ul>
          </div>
          <div class="footerCol">
            <div class="footerColTitle">关于</div>
            <ul>
              <li><a href="#">关于我们</a></li>
              <li><a href="#">隐私政策</a></li>
              <li><a href="#">服务条款</a></li>
              <li><a href="#">联系我们</a></li>
            </ul>
          </div>
        </div>
      </div>
      <div class="footerBottom">
        <span>© 2026 DataClawHub. All Rights Reserved.</span>
        <span>沪 ICP 备 2026023405 号-1</span>
        <a href="#">切回旧版</a>
      </div>
    </div>`;
}

/* ==========================================================================
   共享组件：制品卡片
   ========================================================================== */

/**
 * 获取制品的文件图标（根据类型决定蓝色/橙色）
 * @param {string} type - 制品类型
 * @returns {string} 文件图标 SVG 的 CSS 类
 */
function getFileIconClass(type) {
  return type === 'source' || type === 'dataset' ? 'fileIcon blue' : 'fileIcon';
}

/**
 * 渲染单个制品卡片 HTML
 * @param {Object} item - 制品数据对象
 * @param {number} index - 索引（用于入场动画延迟）
 * @returns {string} 卡片 HTML 字符串
 */
function renderArtifactCard(item, index) {
  const visibilityBadge =
    item.visibility === '公开'
      ? '<span class="badge badgeGreen">公开</span>'
      : '<span class="badge badgeGrey">私有</span>';
  const permissionBadge =
    item.permission === 'AI'
      ? '<span class="badge badgePurple">AI</span>'
      : '<span class="badge badgeOrange">人工</span>';
  const verifiedHtml = item.verified
    ? `<span class="cardVerified">${icons.shield} 已验证</span>`
    : '';
  const costHtml =
    item.cost === '免费'
      ? '<span class="cost">免费</span>'
      : `<span class="cost">${item.cost}</span>`;

  return `
    <article class="artifactCard fadeUp" style="animation-delay:${index * 60}ms">
      <div class="cardTop">
        <span class="typeTag">${item.type}</span>
        ${verifiedHtml}
      </div>
      <a class="cardTitle" href="artifactDetail.html?id=${item.id}">${item.title}</a>
      <div class="cardAuthor">
        <span class="avatar">${item.authorInitial}</span>
        <span class="cardAuthorName">${item.author}</span>
      </div>
      <p class="cardDesc">${item.description}</p>
      <div class="cardFile">
        <span class="${getFileIconClass(item.type)}">${icons.file}</span>
        <div class="fileMeta">
          <div class="fileName">${item.fileName}</div>
          <div class="fileInfo">${item.fileSize} · ${item.fileCount} 个文件</div>
        </div>
        ${permissionBadge}
        ${visibilityBadge}
      </div>
      <div class="cardExec">
        <span class="execItem">${icons.cpu} ${item.tool}</span>
        <span class="execItem">${item.model}</span>
        <span class="execItem">${icons.clock} ${item.duration}</span>
        <span class="execItem">${item.tokens}</span>
        ${costHtml}
      </div>
    </article>`;
}

/**
 * 渲染制品卡片网格到指定容器
 * @param {string} containerId - 目标容器元素 id
 * @param {Array} list - 制品数据数组
 */
function renderArtifactGrid(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = list.map((item, i) => renderArtifactCard(item, i)).join('');
}

/* ==========================================================================
   共享组件：分页
   ========================================================================== */

/**
 * 渲染分页组件
 * @param {number} current - 当前页码（从 1 开始）
 * @param {number} total - 总页数
 * @param {Function} onPageChange - 页码切换回调
 */
function renderPagination(current, total, onPageChange) {
  const container = document.getElementById('pagination');
  if (!container) return;

  const pageArr = [];
  for (let i = 1; i <= total; i++) pageArr.push(i);

  const pagesHtml = pageArr
    .map(
      (p) =>
        `<button class="pageBtn ${p === current ? 'active' : ''}" data-page="${p}">${p}</button>`
    )
    .join('');

  container.innerHTML = `
    <button class="pageBtn ${current === 1 ? 'disabled' : ''}" data-page="${current - 1}">上一页</button>
    ${pagesHtml}
    <button class="pageBtn ${current === total ? 'disabled' : ''}" data-page="${current + 1}">下一页</button>`;

  container.querySelectorAll('.pageBtn').forEach((btn) => {
    btn.addEventListener('click', () => {
      const page = parseInt(btn.dataset.page, 10);
      if (page >= 1 && page <= total) onPageChange(page);
    });
  });
}

/* ==========================================================================
   Toast 提示
   ========================================================================== */

/**
 * 显示一条 Toast 提示
 * @param {string} message - 提示文字
 * @param {string} type - 类型：success / error（可选）
 */
function showToast(message, type) {
  let wrap = document.getElementById('toastWrap');
  if (!wrap) {
    wrap = document.createElement('div');
    wrap.className = 'toastWrap';
    wrap.id = 'toastWrap';
    document.body.appendChild(wrap);
  }
  const toast = document.createElement('div');
  toast.className = `toast ${type || ''}`;
  toast.textContent = message;
  wrap.appendChild(toast);
  // 2.5 秒后自动移除
  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transition = 'opacity 0.3s';
    setTimeout(() => toast.remove(), 300);
  }, 2500);
}

/* ==========================================================================
   页面初始化：DOMContentLoaded 后按页面类型分发
   ========================================================================== */

document.addEventListener('DOMContentLoaded', () => {
  const body = document.body;

  // 登录页初始化
  if (body.dataset.page === 'login') {
    initLoginPage();
    return;
  }

  // 首页初始化
  if (body.dataset.page === 'home') {
    renderNavbar('featured');
    renderFooter();
    renderArtifactGrid('featuredGrid', artifacts.filter((a) => featuredIds.includes(a.id)));
    renderArchOs();
    renderIndustryZones();
    return;
  }

  // 制品库页初始化
  if (body.dataset.page === 'artifacts') {
    initArtifactsPage();
    return;
  }

  // 制品详情页初始化
  if (body.dataset.page === 'detail') {
    initDetailPage();
  }

  // 竞赛页初始化
  if (body.dataset.page === 'competitions') {
    initCompetitionsPage();
  }

  // 任务页初始化
  if (body.dataset.page === 'tasks') {
    initTasksPage();
  }

  // 探索（精选任务）页初始化
  if (body.dataset.page === 'featured') {
    initFeaturedPage();
  }

  // 知识库页初始化
  if (body.dataset.page === 'knowledge') {
    initKnowledgePage();
  }

  // Issues 页初始化
  if (body.dataset.page === 'issues') {
    initIssuesPage();
  }

  // 任务详情页初始化
  if (body.dataset.page === 'taskDetail') {
    initTaskDetailPage();
  }

  // 竞赛详情页初始化
  if (body.dataset.page === 'competitionDetail') {
    initCompetitionDetailPage();
  }

  // 议题详情页初始化
  if (body.dataset.page === 'issueDetail') {
    initIssueDetailPage();
  }
});

/* ==========================================================================
   制品库页逻辑
   ========================================================================== */

/**
 * 初始化制品库页面：渲染导航/页脚/筛选/卡片/分页
 */
function initArtifactsPage() {
  renderNavbar('artifacts');
  renderFooter();
  initFilter();
  renderArtifactGrid('artifactGrid', artifacts);
  renderPagination(1, 3, (page) => {
    showToast(`跳转到第 ${page} 页（原型演示）`, 'success');
  });
}

/**
 * 初始化筛选栏交互：搜索模式切换、格式/排序下拉渲染、搜索、清空
 */
function initFilter() {
  const modeWrap = document.getElementById('searchModeTabs');
  const searchInput = document.getElementById('searchInput');

  // 渲染搜索模式标签（关键词/领域/行业/上传者）
  if (modeWrap) {
    modeWrap.innerHTML = filterOptions.searchModes
      .map(
        (m, i) => `
        <button class="searchModeTab ${i === 0 ? 'active' : ''}" data-key="${m.key}">
          <span>${m.emoji}</span><span>${m.label}</span>
        </button>`
      )
      .join('');

    // 搜索模式切换：更新高亮与输入框占位提示
    modeWrap.querySelectorAll('.searchModeTab').forEach((tab) => {
      tab.addEventListener('click', () => {
        modeWrap.querySelectorAll('.searchModeTab').forEach((t) => t.classList.remove('active'));
        tab.classList.add('active');
        const mode = filterOptions.searchModes.find((m) => m.key === tab.dataset.key);
        if (mode && searchInput) searchInput.placeholder = mode.placeholder;
        showToast(`已切换到「${mode ? mode.label : ''}」搜索模式`, 'success');
      });
    });
  }

  // 渲染格式下拉选项
  const formatSelect = document.getElementById('formatSelect');
  if (formatSelect) {
    formatSelect.innerHTML = filterOptions.formats
      .map((f) => `<option value="${f === '全部' ? '' : f}">${f}</option>`)
      .join('');
  }

  // 渲染排序下拉选项
  const sortSelect = document.getElementById('sortSelect');
  if (sortSelect) {
    sortSelect.innerHTML = filterOptions.sortBy
      .map((s) => `<option value="${s}">${s}</option>`)
      .join('');
  }

  // 搜索按钮
  const searchBtn = document.getElementById('searchBtn');
  if (searchBtn) {
    searchBtn.addEventListener('click', () => {
      const keyword = (searchInput ? searchInput.value : '').trim();
      showToast(keyword ? `已搜索「${keyword}」（原型演示）` : '请输入搜索关键词', 'success');
    });
  }

  // 清空按钮
  const clearBtn = document.getElementById('clearBtn');
  if (clearBtn) {
    clearBtn.addEventListener('click', () => {
      if (searchInput) searchInput.value = '';
      modeWrap?.querySelectorAll('.searchModeTab').forEach((t) => t.classList.remove('active'));
      modeWrap?.querySelector('.searchModeTab')?.classList.add('active');
      if (searchInput) searchInput.placeholder = filterOptions.searchModes[0].placeholder;
      showToast('已清空筛选条件', 'success');
    });
  }

  // 格式下拉切换
  if (formatSelect) {
    formatSelect.addEventListener('change', () => {
      showToast(`已筛选格式：${formatSelect.value || '全部'}`, 'success');
    });
  }

  // 排序下拉切换
  if (sortSelect) {
    sortSelect.addEventListener('change', () => {
      showToast(`已切换排序：${sortSelect.value}`, 'success');
    });
  }

  // 已验证 / 有视频复选框
  ['verifiedCheck', 'videoCheck'].forEach((id) => {
    const box = document.getElementById(id);
    if (box) {
      box.addEventListener('change', () => {
        const label = id === 'verifiedCheck' ? '已验证' : '有视频';
        showToast(`${label}：${box.checked ? '已勾选' : '已取消'}`, 'success');
      });
    }
  });
}

/* ==========================================================================
   制品详情页逻辑
   ========================================================================== */

/**
 * 初始化制品详情页：解析 id 参数并渲染详情内容
 */
function initDetailPage() {
  renderNavbar('artifacts');
  renderFooter();

  // 从 URL 参数读取制品 id，默认 a1
  const params = new URLSearchParams(window.location.search);
  const id = params.get('id') || 'a1';
  const item = artifacts.find((a) => a.id === id) || artifacts[0];

  renderDetail(item);
}

/**
 * 渲染详情页主体内容
 * @param {Object} item - 制品数据对象
 */
function renderDetail(item) {
  const typeTag = document.getElementById('detailTypeTag');
  const title = document.getElementById('detailTitle');
  const author = document.getElementById('detailAuthor');
  const meta = document.getElementById('detailMeta');
  const body = document.getElementById('detailBody');
  const fileList = document.getElementById('fileList');
  const sidePanel = document.getElementById('sidePanel');
  const relatedBox = document.getElementById('relatedBox');

  // 更新权限 / 可见性 / 验证徽章
  const permBadge = document.getElementById('detailPermBadge');
  const visBadge = document.getElementById('detailVisBadge');
  const verifiedBadge = document.getElementById('detailVerifiedBadge');
  if (permBadge) {
    permBadge.textContent = item.permission;
    permBadge.className = item.permission === 'AI' ? 'badge badgePurple' : 'badge badgeOrange';
  }
  if (visBadge) {
    visBadge.textContent = item.visibility;
    visBadge.className = item.visibility === '公开' ? 'badge badgeGreen' : 'badge badgeGrey';
  }
  if (verifiedBadge) {
    verifiedBadge.style.display = item.verified ? '' : 'none';
  }

  if (typeTag) typeTag.textContent = item.type;
  if (title) title.textContent = item.title;
  if (author) {
    author.innerHTML = `
      <span class="avatar">${item.authorInitial}</span>
      <span class="cardAuthorName">${item.author}</span>`;
  }
  if (meta) {
    meta.innerHTML = `
      <span>${item.fileSize}</span><span class="sepDot"></span>
      <span>${item.fileCount} 个文件</span><span class="sepDot"></span>
      <span>${item.domain}</span><span class="sepDot"></span>
      <span>${item.industry}</span>`;
  }
  if (body) {
    body.innerHTML = `
      <p>${item.description}</p>
      <h3>制品说明</h3>
      <p>本制品面向「${item.domain}」场景，聚焦「${item.industry}」细分领域。数据/代码已按通用许可证开放，可自由下载、复用与二次开发，引用时请注明来源 DataClawHub 与作者 ${item.author}。</p>
      <p>运行环境依赖已在文件包内附带的 README 中说明；如需在云端直接执行，可通过 <a href="#" style="color:var(--accent)">云端工作台</a> 一键拉起。`;
  }
  if (fileList) {
    fileList.innerHTML = `
      <div class="fileListItem">
        <span class="${getFileIconClass(item.type)}">${icons.file}</span>
        <div class="fileListInfo">
          <div class="fileListName">${item.fileName}</div>
          <div class="fileListMeta">${item.fileSize} · 主文件</div>
        </div>
        <a class="btn btnPrimary" href="#" onclick="showToast('开始下载（原型演示）','success');return false;">${icons.download} 下载</a>
      </div>
      <div class="fileListItem">
        <span class="fileIcon blue">${icons.folder}</span>
        <div class="fileListInfo">
          <div class="fileListName">README.md</div>
          <div class="fileListMeta">使用说明 · 4.2 KB</div>
        </div>
        <span class="badge badgeBlue">附送</span>
      </div>`;
  }

  // AI 评价区
  renderAIEvaluation(item);

  // 侧边栏：上传者信息 + 统计
  if (sidePanel) {
    sidePanel.innerHTML = `
      <div class="sideCard">
        <div class="sideCardTitle">上传者</div>
        <div class="sideUploader">
          <span class="avatar">${item.authorInitial}</span>
          <div>
            <div class="sideUploaderName">${item.author}</div>
            <div class="sideUploaderRole">物流 AI 贡献者</div>
          </div>
        </div>
        <button class="btn btnPrimary" style="width:100%">关注</button>
      </div>
      <div class="sideCard">
        <div class="sideCardTitle">制品统计</div>
        <div class="statRow"><span class="k">下载量</span><span class="v">${item.downloads}</span></div>
        <div class="statRow"><span class="k">点赞</span><span class="v">${item.likes}</span></div>
        <div class="statRow"><span class="k">预估费用</span><span class="v highlight">${item.cost}</span></div>
        <div class="statRow"><span class="k">执行工具</span><span class="v">${item.tool}</span></div>
        <div class="statRow"><span class="k">大模型</span><span class="v">${item.model}</span></div>
      </div>`;
  }

  // 相关制品
  if (relatedBox) {
    const related = artifacts.filter((a) => a.id !== item.id).slice(0, 3);
    relatedBox.innerHTML = related
      .map(
        (r) => `
        <a class="relatedItem" href="artifactDetail.html?id=${r.id}">
          <span class="${getFileIconClass(r.type)}">${icons.file}</span>
          <div>
            <div class="relatedTitle">${r.title}</div>
            <div class="relatedMeta">${r.downloads} 次下载 · ${r.likes} 赞</div>
          </div>
        </a>`
      )
      .join('');
  }

  // 点赞按钮交互
  const likeBtn = document.getElementById('likeBtn');
  if (likeBtn) {
    let liked = false;
    likeBtn.classList.add('btnGhost');
    likeBtn.innerHTML = `${icons.heart} 点赞 ${item.likes}`;
    likeBtn.addEventListener('click', () => {
      liked = !liked;
      likeBtn.classList.toggle('btnPrimary', liked);
      likeBtn.classList.toggle('btnGhost', !liked);
      likeBtn.innerHTML = liked
        ? `${icons.heart} 已点赞 ${item.likes + 1}`
        : `${icons.heart} 点赞 ${item.likes}`;
      showToast(liked ? '点赞成功' : '已取消点赞', 'success');
    });
  }

  // 评论提交
  const commentBtn = document.getElementById('commentBtn');
  if (commentBtn) {
    commentBtn.addEventListener('click', () => {
      const textarea = document.getElementById('commentInput');
      if (textarea && textarea.value.trim()) {
        showToast('评论已提交（原型演示）', 'success');
        textarea.value = '';
      } else {
        showToast('请输入评论内容', 'error');
      }
    });
  }
}

/**
 * 渲染制品 AI 评价区（对齐源网站 ai_evaluation：综合评分 + 四维评分 + 总结）
 * @param {Object} item - 制品数据对象
 */
function renderAIEvaluation(item) {
  const container = document.getElementById('aiEvaluation');
  if (!container) return;

  const eva = artifactEvaluations[item.id];
  // 无评价数据时渲染「评价中」占位
  if (!eva || eva.status !== 'completed') {
    container.innerHTML = `
      <div class="aiEvalSection">
        <div class="aiEvalHeader">
          <span class="aiEvalTitle">${icons.cpu} AI 评价</span>
          <span class="badge badgeGrey">评价中</span>
        </div>
        <p class="aiEvalPending">AI 正在对该制品进行自动化评测，完成后将展示综合评分与四维评价结果。</p>
      </div>`;
    return;
  }

  // 四维评分维度配置（顺序与源网站一致）
  const dimConfig = [
    { key: 'requirementFit', label: '需求符合度' },
    { key: 'completeness', label: '完整性' },
    { key: 'quality', label: '质量' },
    { key: 'usability', label: '可用性' }
  ];

  // 根据总分生成评分等级徽章样式
  const scoreClass = eva.overallScore >= 90 ? 'badgeGreen' : eva.overallScore >= 70 ? 'badgeBlue' : eva.overallScore >= 50 ? 'badgeOrange' : 'badgeGrey';

  container.innerHTML = `
    <div class="aiEvalSection">
      <div class="aiEvalHeader">
        <span class="aiEvalTitle">${icons.cpu} AI 评价</span>
        <span class="badge ${scoreClass}">综合 ${eva.overallScore} 分</span>
      </div>

      <div class="aiEvalGrid">
        ${dimConfig
          .map((d) => {
            const dim = eva.dimensions[d.key];
            const dimBarClass =
              dim.score >= 90 ? 'barGreen' : dim.score >= 70 ? 'barBlue' : dim.score >= 50 ? 'barOrange' : 'barGrey';
            return `
          <div class="aiEvalDim">
            <div class="aiEvalDimHead">
              <span class="aiEvalDimName">${d.label}</span>
              <span class="aiEvalDimScore">${dim.score}</span>
            </div>
            <div class="aiEvalBar"><span class="${dimBarClass}" style="width:${dim.score}%"></span></div>
            <p class="aiEvalDimComment">${dim.comment}</p>
          </div>`;
          })
          .join('')}
      </div>

      <div class="aiEvalSummary">
        <div class="aiEvalSummaryLabel">AI 总结</div>
        <p class="aiEvalSummaryText">${eva.summary}</p>
      </div>
    </div>`;
}

/* ==========================================================================
   登录页逻辑
   ========================================================================== */

/**
 * 初始化登录页：模式切换与表单提交
 */
function initLoginPage() {
  // 经典 / Terminal 模式切换
  const tabs = document.querySelectorAll('.loginTab');
  const classicForm = document.getElementById('classicForm');
  const terminalPanel = document.getElementById('terminalPanel');

  tabs.forEach((tab) => {
    tab.addEventListener('click', () => {
      tabs.forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');
      const mode = tab.dataset.mode;
      if (mode === 'classic') {
        classicForm.style.display = 'block';
        terminalPanel.style.display = 'none';
      } else {
        classicForm.style.display = 'none';
        terminalPanel.style.display = 'block';
      }
    });
  });

  // 登录按钮
  const loginBtn = document.getElementById('loginBtn');
  if (loginBtn) {
    loginBtn.addEventListener('click', () => {
      showToast('登录成功，欢迎回来（原型演示）', 'success');
      setTimeout(() => (window.location.href = 'index.html'), 800);
    });
  }
}

/* ==========================================================================
   竞赛页逻辑
   ========================================================================== */

/**
 * 渲染单个竞赛卡片 HTML
 * @param {Object} item - 竞赛数据对象
 * @param {number} index - 索引（用于入场动画延迟）
 * @returns {string} 卡片 HTML 字符串
 */
function renderCompetitionCard(item, index) {
  // 状态徽章样式映射
  const statusClass = {
    '即将开始': 'badgePurple',
    '报名中': 'badgeBlue',
    '进行中': 'badgeGreen',
    '已结束': 'badgeGrey'
  }[item.status] || 'badgeGrey';

  const actionHtml =
    item.status === '已结束'
      ? '<span class="badge badgeGrey" style="padding:8px 16px;font-size:13px">已结束</span>'
      : `<a class="btn btnPrimary" href="#" onclick="showToast('报名成功（原型演示）','success');return false;">立即报名</a>`;

  return `
    <article class="artifactCard competitionCard fadeUp" style="animation-delay:${index * 60}ms">
      <div class="cardTop">
        <span class="typeTag">${item.category}</span>
        <span class="badge ${statusClass}">${item.status}</span>
      </div>
      <a class="cardTitle" href="competitionDetail.html?id=${item.id}">${item.title}</a>
      <p class="cardDesc">${item.description}</p>
      <div class="competitionMeta">
        <span>${item.startDate} ~ ${item.endDate}</span>
        <span class="competitionReward">${icons.coin} ${item.reward}</span>
        <span>${icons.eye} ${item.participants} 人参与</span>
      </div>
      <div class="competitionFooter">${actionHtml}</div>
    </article>`;
}

/**
 * 渲染竞赛卡片网格到指定容器
 * @param {string} containerId - 目标容器 id
 * @param {Array} list - 竞赛数据数组
 */
function renderCompetitionGrid(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = list.map((item, i) => renderCompetitionCard(item, i)).join('');
}

/**
 * 竞赛页筛选状态（领域分类 + 状态 + 搜索关键词 + 排序）
 */
let competitionFilterState = {
  category: '全部领域',
  status: '全部',
  keyword: '',
  sort: '默认排序'
};

/**
 * 渲染竞赛赛事预告区：展示「即将开始」的赛事，无则显示空态
 */
function renderCompetitionUpcoming() {
  const listWrap = document.getElementById('upcomingList');
  const emptyWrap = document.getElementById('upcomingEmpty');
  const upcoming = competitions.filter((c) => c.status === '即将开始');

  if (!listWrap || !emptyWrap) return;
  if (upcoming.length === 0) {
    listWrap.innerHTML = '';
    listWrap.style.display = 'none';
    emptyWrap.style.display = '';
    return;
  }
  emptyWrap.style.display = 'none';
  listWrap.style.display = 'grid';
  listWrap.innerHTML = upcoming.map((item, i) => renderCompetitionCard(item, i)).join('');
}

/**
 * 根据当前筛选状态计算竞赛列表
 * @returns {Array} 过滤后的竞赛数组
 */
function applyCompetitionFilter() {
  let list = competitions.slice();

  // 领域分类筛选
  if (competitionFilterState.category !== '全部领域') {
    list = list.filter((c) => c.category === competitionFilterState.category);
  }

  // 状态筛选（已关注/已报名为布尔维度，其余为 status 字段）
  const st = competitionFilterState.status;
  if (st === '已关注') {
    list = list.filter((c) => c.followed);
  } else if (st === '已报名') {
    list = list.filter((c) => c.registered);
  } else if (st !== '全部') {
    list = list.filter((c) => c.status === st);
  }

  // 关键词搜索（按标题模糊匹配）
  const kw = competitionFilterState.keyword.trim().toLowerCase();
  if (kw) {
    list = list.filter((c) => c.title.toLowerCase().includes(kw));
  }

  // 排序
  const sort = competitionFilterState.sort;
  if (sort === '截止时间') {
    list = list.slice().sort((a, b) => (a.endDate > b.endDate ? 1 : -1));
  } else if (sort === '奖金') {
    list = list.slice().sort((a, b) => parseReward(b.reward) - parseReward(a.reward));
  } else if (sort === '参与人数') {
    list = list.slice().sort((a, b) => b.participants - a.participants);
  }

  return list;
}

/**
 * 从奖金字符串解析数值（如 "¥50,000" -> 50000）
 * @param {string} reward - 奖金字符串
 * @returns {number} 解析后的数值
 */
function parseReward(reward) {
  return parseInt(String(reward).replace(/[^\d]/g, ''), 10) || 0;
}

/**
 * 初始化竞赛页面：渲染导航/页脚/预告/领域/状态/搜索/排序/卡片
 */
function initCompetitionsPage() {
  renderNavbar('competitions');
  renderFooter();

  // 渲染赛事预告区
  renderCompetitionUpcoming();

  // 渲染排序下拉
  const sortSelect = document.getElementById('competitionSortSelect');
  if (sortSelect) {
    sortSelect.innerHTML = competitionSortOptions
      .map((s) => `<option value="${s}">${s}</option>`)
      .join('');
    sortSelect.addEventListener('change', () => {
      competitionFilterState.sort = sortSelect.value;
      renderCompetitionGrid('competitionGrid', applyCompetitionFilter());
    });
  }

  // 渲染领域分类栏
  const categoryBar = document.getElementById('competitionCategoryBar');
  if (categoryBar) {
    categoryBar.innerHTML = competitionCategories
      .map(
        (c) =>
          `<button class="categoryBtn ${c === '全部领域' ? 'active' : ''}" data-category="${c}">${c}</button>`
      )
      .join('');
    categoryBar.querySelectorAll('.categoryBtn').forEach((btn) => {
      btn.addEventListener('click', () => {
        categoryBar.querySelectorAll('.categoryBtn').forEach((b) => b.classList.remove('active'));
        btn.classList.add('active');
        competitionFilterState.category = btn.dataset.category;
        renderCompetitionGrid('competitionGrid', applyCompetitionFilter());
      });
    });
  }

  // 渲染状态筛选栏
  const statusBar = document.getElementById('competitionStatusBar');
  if (statusBar) {
    statusBar.innerHTML = competitionStatusFilters
      .map(
        (s) =>
          `<button class="statusBtn ${s === '全部' ? 'active' : ''}" data-status="${s}">${s}</button>`
      )
      .join('');
    statusBar.querySelectorAll('.statusBtn').forEach((btn) => {
      btn.addEventListener('click', () => {
        statusBar.querySelectorAll('.statusBtn').forEach((b) => b.classList.remove('active'));
        btn.classList.add('active');
        competitionFilterState.status = btn.dataset.status;
        renderCompetitionGrid('competitionGrid', applyCompetitionFilter());
      });
    });
  }

  // 搜索输入（实时过滤）
  const searchInput = document.getElementById('competitionSearchInput');
  if (searchInput) {
    searchInput.addEventListener('input', () => {
      competitionFilterState.keyword = searchInput.value;
      renderCompetitionGrid('competitionGrid', applyCompetitionFilter());
    });
  }

  // 渲染竞赛卡片网格
  renderCompetitionGrid('competitionGrid', competitions);
}

/* ==========================================================================
   任务页逻辑
   ========================================================================== */

/**
 * 渲染单个任务卡片 HTML
 * @param {Object} item - 任务数据对象
 * @param {number} index - 索引（用于入场动画延迟）
 * @returns {string} 卡片 HTML 字符串
 */
function renderTaskCard(item, index) {
  // 任务状态徽章样式映射
  const statusClass = {
    '进行中': 'badgeGreen',
    '已完成': 'badgeBlue',
    '已关闭': 'badgeGrey'
  }[item.status] || 'badgeGrey';

  let actionHtml;
  if (item.status === '已关闭') {
    actionHtml = '<span class="badge badgeGrey" style="padding:8px 16px;font-size:13px">已关闭</span>';
  } else if (item.status === '已完成') {
    actionHtml = `<a class="btn btnGhost" href="#" onclick="showToast('查看成果（原型演示）','success');return false;">查看成果</a>`;
  } else {
    actionHtml = `<a class="btn btnPrimary" href="#" onclick="showToast('认领任务成功（原型演示）','success');return false;">认领任务</a>`;
  }

  return `
    <article class="artifactCard competitionCard fadeUp" style="animation-delay:${index * 60}ms">
      <div class="cardTop">
        <span class="typeTag">${item.domain}</span>
        <span class="badge ${statusClass}">${item.status}</span>
      </div>
      <a class="cardTitle" href="taskDetail.html?id=${item.id}">${item.title}</a>
      <p class="cardDesc">${item.description}</p>
      <div class="competitionMeta">
        <span>${icons.clock} 截止 ${item.deadline}</span>
        <span class="competitionReward">${icons.coin} ${item.reward}</span>
        <span>${icons.eye} ${item.participants} 人参与</span>
      </div>
      <div class="competitionFooter">${actionHtml}</div>
    </article>`;
}

/**
 * 渲染任务卡片网格到指定容器
 * @param {string} containerId - 目标容器 id
 * @param {Array} list - 任务数据数组
 */
function renderTaskGrid(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = list.map((item, i) => renderTaskCard(item, i)).join('');
}

/**
 * 初始化任务页面：渲染导航/页脚/卡片与状态筛选
 */
function initTasksPage() {
  renderNavbar('tasks');
  renderFooter();
  renderTaskGrid('taskGrid', tasks);

  // 状态筛选标签
  const tabs = document.querySelectorAll('#taskTabs .filterTab');
  tabs.forEach((tab) => {
    tab.addEventListener('click', () => {
      tabs.forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');
      const status = tab.dataset.status;
      const list =
        status === '全部'
          ? tasks
          : tasks.filter((t) => t.status === status);
      renderTaskGrid('taskGrid', list);
    });
  });
}

/* ==========================================================================
   首页 arch.os 智能目录系统与行业专区渲染
   ========================================================================== */

/**
 * 渲染 arch.os 智能目录系统特性卡片
 */
function renderArchOs() {
  const container = document.getElementById('archOsGrid');
  if (!container) return;
  container.innerHTML = archOsFeatures
    .map(
      (f, i) => `
      <div class="archOsCard fadeUp" style="animation-delay:${i * 80}ms">
        <div class="archOsIcon">${icons[f.icon]}</div>
        <div class="archOsTitle">${f.title}</div>
        <p class="archOsDesc">${f.desc}</p>
      </div>`
    )
    .join('');
}

/**
 * 渲染行业专区标签卡片
 */
function renderIndustryZones() {
  const container = document.getElementById('industryGrid');
  if (!container) return;
  container.innerHTML = industryZones
    .map(
      (z, i) => `
      <a class="industryCard fadeUp" style="animation-delay:${i * 40}ms" href="#"
         onclick="showToast('进入「${z.name}」专区（原型演示）','success');return false;">
        <span class="industryName">${z.name}</span>
        <span class="industryCount">${z.count} 个制品</span>
      </a>`
    )
    .join('');
}

/* ==========================================================================
   探索页（精选任务）逻辑
   ========================================================================== */

/**
 * 渲染精选任务卡片（复用任务卡片结构，附加精选维度标签）
 * @param {Object} item - 任务数据对象
 * @param {number} index - 索引（用于入场动画延迟）
 * @returns {string} 卡片 HTML 字符串
 */
function renderFeaturedTaskCard(item, index) {
  // 任务状态徽章样式映射
  const statusClass = {
    '进行中': 'badgeGreen',
    '已完成': 'badgeBlue',
    '已关闭': 'badgeGrey'
  }[item.status] || 'badgeGrey';

  return `
    <article class="artifactCard competitionCard fadeUp" style="animation-delay:${index * 60}ms">
      <div class="cardTop">
        <span class="typeTag">${item.domain}</span>
        <span class="badge badgeOrange">${item.tag}</span>
        <span class="badge ${statusClass}">${item.status}</span>
      </div>
      <a class="cardTitle" href="taskDetail.html?id=${item.id}">${item.title}</a>
      <p class="cardDesc">${item.description}</p>
      <div class="competitionMeta">
        <span>${icons.clock} 截止 ${item.deadline}</span>
        <span class="competitionReward">${icons.coin} ${item.reward}</span>
        <span>${icons.eye} ${item.participants} 人参与</span>
      </div>
      <div class="competitionFooter">
        <a class="btn btnPrimary" href="taskDetail.html?id=${item.id}">认领任务</a>
      </div>
    </article>`;
}

/**
 * 渲染精选任务网格到指定容器
 * @param {string} containerId - 目标容器 id
 * @param {Array} list - 任务数据数组
 */
function renderFeaturedGrid(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = list
    .map((item, i) => renderFeaturedTaskCard(item, i))
    .join('');
}

/**
 * 启动倒计时刷新（探索页顶部「下次刷新」）
 * @param {number} totalSeconds - 倒计时总秒数
 */
function startCountdown(totalSeconds) {
  const el = document.getElementById('countdownText');
  if (!el) return;
  let remain = totalSeconds;
  const tick = () => {
    const mm = String(Math.floor(remain / 60)).padStart(2, '0');
    const ss = String(remain % 60).padStart(2, '0');
    el.textContent = `${mm}:${ss}`;
    if (remain > 0) {
      remain -= 1;
      setTimeout(tick, 1000);
    } else {
      el.textContent = '刷新中...';
    }
  };
  tick();
}

/**
 * 初始化探索页（精选任务）：渲染导航/页脚/倒计时/筛选标签/卡片
 */
function initFeaturedPage() {
  renderNavbar('featured');
  renderFooter();
  renderFeaturedGrid('featuredTaskGrid', tasks);

  // 启动倒计时（30 分钟）
  startCountdown(30 * 60);

  // 精选维度筛选标签
  const tabs = document.querySelectorAll('#featuredTabs .filterTab');
  tabs.forEach((tab) => {
    tab.addEventListener('click', () => {
      tabs.forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');
      const tag = tab.dataset.tag;
      const list = tag === '全部' ? tasks : tasks.filter((t) => t.tag === tag);
      renderFeaturedGrid('featuredTaskGrid', list);
    });
  });

  // 手动刷新按钮
  const refreshBtn = document.getElementById('refreshBtn');
  if (refreshBtn) {
    refreshBtn.addEventListener('click', () => {
      showToast('正在刷新精选任务（原型演示）', 'success');
      startCountdown(30 * 60);
    });
  }
}

/* ==========================================================================
   知识库页逻辑
   ========================================================================== */

/**
 * 渲染知识库文档列表到指定容器
 * @param {string} containerId - 目标容器 id
 * @param {Array} list - 文档数据数组
 */
function renderKnowledgeList(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;
  container.innerHTML = list
    .map(
      (d, i) => `
      <div class="knowledgeItem fadeUp" style="animation-delay:${i * 60}ms">
        <span class="knowledgeIcon">${icons.file}</span>
        <div class="knowledgeInfo">
          <a class="knowledgeTitle" href="#" onclick="showToast('打开文档（原型演示）','success');return false;">${d.title}</a>
          <div class="knowledgeMeta">${d.category} · ${d.size} · ${d.date}</div>
        </div>
        <span class="knowledgeViews">${icons.eye} ${d.views}</span>
      </div>`
    )
    .join('');
}

/**
 * 初始化知识库页面：渲染导航/页脚/文档列表与上传、智能问答交互
 */
function initKnowledgePage() {
  renderNavbar('knowledge');
  renderFooter();
  renderKnowledgeList('knowledgeList', knowledgeDocs);

  // 上传文档按钮
  const uploadBtn = document.getElementById('knowledgeUploadBtn');
  if (uploadBtn) {
    uploadBtn.addEventListener('click', () => {
      showToast('文档上传功能（原型演示）', 'success');
    });
  }

  // 智能问答提交
  const askBtn = document.getElementById('askBtn');
  const askInput = document.getElementById('askInput');
  if (askBtn && askInput) {
    askBtn.addEventListener('click', () => {
      const q = askInput.value.trim();
      if (q) {
        showToast(`已提问「${q}」（原型演示）`, 'success');
        askInput.value = '';
      } else {
        showToast('请输入问题', 'error');
      }
    });
  }
}

/* ==========================================================================
   Issues 页逻辑
   ========================================================================== */

/**
 * 渲染 Issues 列表到指定容器
 * @param {string} containerId - 目标容器 id
 * @param {Array} list - 议题数据数组
 */
function renderIssuesList(containerId, list) {
  const container = document.getElementById(containerId);
  if (!container) return;

  // 议题类型徽章样式映射
  const typeClass = { 建议: 'badgeBlue', 问题: 'badgeOrange', 议题: 'badgePurple' };

  container.innerHTML = list
    .map(
      (it, i) => `
      <div class="issueItem fadeUp" style="animation-delay:${i * 60}ms">
        <span class="avatar">${it.authorInitial}</span>
        <div class="issueInfo">
          <a class="issueTitle" href="issueDetail.html?id=${it.id}">${it.title}</a>
          <div class="issueMeta">
            <span class="badge ${typeClass[it.type] || 'badgeGrey'}">${it.type}</span>
            <span>${it.author} · ${it.time}</span>
          </div>
        </div>
        <div class="issueRight">
          <span class="badge ${it.status === '开放' ? 'badgeGreen' : 'badgeGrey'}">${it.status}</span>
          <span class="issueComments">${icons.comment} ${it.comments}</span>
        </div>
      </div>`
    )
    .join('');
}

/**
 * 初始化 Issues 页面：渲染导航/页脚/计数/列表与发起议题交互
 */
function initIssuesPage() {
  renderNavbar('issues');
  renderFooter();

  // 更新议题总数
  const countEl = document.getElementById('issueCount');
  if (countEl) countEl.textContent = `共 ${issuesData.length} 个`;

  renderIssuesList('issuesList', issuesData);

  // 发起议题按钮
  const newBtn = document.getElementById('newIssueBtn');
  if (newBtn) {
    newBtn.addEventListener('click', () => {
      showToast('发起议题（原型演示）', 'success');
    });
  }

  // 状态筛选标签
  const tabs = document.querySelectorAll('#issueTabs .filterTab');
  tabs.forEach((tab) => {
    tab.addEventListener('click', () => {
      tabs.forEach((t) => t.classList.remove('active'));
      tab.classList.add('active');
      const status = tab.dataset.status;
      const list =
        status === '全部'
          ? issuesData
          : issuesData.filter((i) => i.status === status);
      renderIssuesList('issuesList', list);
    });
  });
}

/* ==========================================================================
   任务详情页逻辑
   ========================================================================== */

/**
 * 任务状态徽章样式映射
 * @param {string} status - 任务状态
 * @returns {string} badge 样式类名
 */
function taskStatusClass(status) {
  return { '进行中': 'badgeGreen', '已完成': 'badgeBlue', '已关闭': 'badgeGrey' }[status] || 'badgeGrey';
}

/**
 * 初始化任务详情页：渲染导航/页脚，并按 URL id 渲染详情
 */
function initTaskDetailPage() {
  renderNavbar('tasks');
  renderFooter();

  const params = new URLSearchParams(window.location.search);
  const id = params.get('id') || 't1';
  const item = tasks.find((t) => t.id === id) || tasks[0];

  renderTaskDetail(item);
}

/**
 * 渲染任务详情页主体与侧边栏
 * @param {Object} item - 任务数据对象
 */
function renderTaskDetail(item) {
  const main = document.getElementById('taskMain');
  const side = document.getElementById('taskSide');

  // 关联制品：按 relatedArtifactIds 映射到 artifacts
  const related = (item.relatedArtifactIds || [])
    .map((rid) => artifacts.find((a) => a.id === rid))
    .filter(Boolean);

  if (main) {
    const criteriaHtml = (item.acceptanceCriteria || [])
      .map(
        (c, i) => `
        <div class="acceptItem">
          <span class="acceptIndex">${i + 1}</span>
          <span class="acceptText">${c}</span>
        </div>`
      )
      .join('');

    const bonusHtml = (item.bonusContributors || []).length
      ? (item.bonusContributors || [])
          .map(
            (b) => `
          <div class="bonusContributor">
            <span class="avatar">${b.initial}</span>
            <span class="bonusName">${b.name}</span>
            <span class="bonusAmount">${icons.coin} ${b.amount}</span>
          </div>`
          )
          .join('')
      : '<p class="emptyHint">暂无加赏贡献者</p>';

    const relatedHtml = related.length
      ? related
          .map(
            (r) => `
          <a class="relatedItem" href="artifactDetail.html?id=${r.id}">
            <span class="${getFileIconClass(r.type)}">${icons.file}</span>
            <div>
              <div class="relatedTitle">${r.title}</div>
              <div class="relatedMeta">${r.type} · ${r.downloads} 次下载</div>
            </div>
          </a>`
          )
          .join('')
      : '<p class="emptyHint">暂无关联制品</p>';

    main.innerHTML = `
      <div class="detailType">
        <span class="typeTag">${item.domain}</span>
        <span class="badge ${taskStatusClass(item.status)}">${item.status}</span>
        <span class="badge badgePurple">${item.taskType}</span>
        ${item.tag ? `<span class="badge badgeOrange">${item.tag}</span>` : ''}
      </div>

      <h1 class="detailTitle">${item.title}</h1>

      <div class="detailMeta">
        <span>${icons.clock} 截止 ${item.deadline}</span>
        <span class="sepDot"></span>
        <span>${icons.eye} ${item.participants} 人参与</span>
      </div>

      <div class="detailBody">
        <p>${item.description}</p>
        <h3>任务信息</h3>
        <div class="taskMetaGrid">
          <div class="taskMetaItem"><span class="taskMetaLabel">任务类型</span><span class="taskMetaValue">${item.taskType}</span></div>
          <div class="taskMetaItem"><span class="taskMetaLabel">所属行业</span><span class="taskMetaValue">${item.industry}</span></div>
          <div class="taskMetaItem"><span class="taskMetaLabel">交付格式</span><span class="taskMetaValue">${item.expectedFormat}</span></div>
          <div class="taskMetaItem"><span class="taskMetaLabel">基础报酬</span><span class="taskMetaValue highlight">¥${item.rewardAmount}</span></div>
          <div class="taskMetaItem"><span class="taskMetaLabel">加赏奖励</span><span class="taskMetaValue highlight">¥${item.bonusReward}</span></div>
          <div class="taskMetaItem"><span class="taskMetaLabel">截止时间</span><span class="taskMetaValue">${item.deadline}</span></div>
        </div>

        <h3>验收标准</h3>
        <div class="acceptList">${criteriaHtml}</div>

        <h3>关联制品</h3>
        <div class="relatedList">${relatedHtml}</div>

        <h3>加赏贡献者</h3>
        <div class="bonusList">${bonusHtml}</div>
      </div>`;
  }

  if (side) {
    side.innerHTML = `
      <div class="sideCard">
        <div class="sideCardTitle">任务奖励</div>
        <div class="rewardHero">
          <div class="rewardMain">${icons.coin} ¥${item.rewardAmount}</div>
          <div class="rewardSub">基础报酬</div>
        </div>
        ${item.bonusReward ? `<div class="statRow"><span class="k">加赏奖励</span><span class="v highlight">¥${item.bonusReward}</span></div>` : ''}
        <a class="btn btnPrimary" style="width:100%;margin-top:12px" href="#" onclick="showToast('认领任务成功（原型演示）','success');return false;">认领任务</a>
      </div>
      <div class="sideCard">
        <div class="sideCardTitle">任务统计</div>
        <div class="statRow"><span class="k">执行次数</span><span class="v">${item.executionCount}</span></div>
        <div class="statRow"><span class="k">提交成果</span><span class="v">${item.submissionCount}</span></div>
        <div class="statRow"><span class="k">分享</span><span class="v">${item.sharesCount}</span></div>
        <div class="statRow"><span class="k">浏览</span><span class="v">${item.viewsCount}</span></div>
        <div class="statRow"><span class="k">点赞</span><span class="v">${item.likesCount}</span></div>
        <div class="statRow"><span class="k">评论</span><span class="v">${item.commentsCount}</span></div>
        <div class="statRow"><span class="k">收藏</span><span class="v">${item.favoritesCount}</span></div>
      </div>`;
  }
}

/* ==========================================================================
   竞赛详情页逻辑
   ========================================================================== */

/**
 * 竞赛状态徽章样式映射
 * @param {string} status - 竞赛状态
 * @returns {string} badge 样式类名
 */
function competitionStatusClass(status) {
  return {
    '即将开始': 'badgePurple',
    '报名中': 'badgeBlue',
    '进行中': 'badgeGreen',
    '已结束': 'badgeGrey'
  }[status] || 'badgeGrey';
}

/**
 * 初始化竞赛详情页：渲染导航/页脚，并按 URL id 渲染详情
 */
function initCompetitionDetailPage() {
  renderNavbar('competitions');
  renderFooter();

  const params = new URLSearchParams(window.location.search);
  const id = params.get('id') || 'c1';
  const item = competitions.find((c) => c.id === id) || competitions[0];

  renderCompetitionDetail(item);
}

/**
 * 渲染竞赛详情页主体与侧边栏
 * @param {Object} item - 竞赛数据对象
 */
function renderCompetitionDetail(item) {
  const main = document.getElementById('competitionMain');
  const side = document.getElementById('competitionSide');

  if (main) {
    const tagsHtml = (item.tags || [])
      .map((t) => `<span class="badge badgeBlue">${t}</span>`)
      .join('');

    const roundsHtml = (item.rounds || [])
      .map(
        (r, i) => `
        <div class="timelineItem">
          <span class="timelineDot">${i + 1}</span>
          <div class="timelineBody">
            <div class="timelineName">${r.name}</div>
            <div class="timelineDate">${r.date}</div>
            <p class="timelineDesc">${r.desc}</p>
          </div>
        </div>`
      )
      .join('');

    const rulesHtml = (item.rules || [])
      .map((r) => `<li class="ruleItem">${r}</li>`)
      .join('');

    const criteriaHtml = (item.evaluationCriteria || [])
      .map((c) => `<li class="ruleItem">${c}</li>`)
      .join('');

    const weightsHtml = (item.scoringWeights || [])
      .map(
        (w) => `
        <div class="weightRow">
          <span class="weightName">${w.name}</span>
          <div class="weightBar"><span style="width:${w.weight}%"></span></div>
          <span class="weightValue">${w.weight}%</span>
        </div>`
      )
      .join('');

    const leaderboardHtml = (item.leaderboard || []).length
      ? `
        <div class="leaderboardTable">
          <div class="leaderboardRow leaderboardHead">
            <span>排名</span><span>团队</span><span>得分</span>
          </div>
          ${(item.leaderboard || [])
            .map(
              (r) => `
            <div class="leaderboardRow">
              <span class="leaderboardRank ${r.rank <= 3 ? 'top' : ''}">${r.rank}</span>
              <span>${r.team}</span>
              <span class="leaderboardScore">${r.score}</span>
            </div>`
            )
            .join('')}
        </div>`
      : '<p class="emptyHint">排行榜将在比赛开始后公布</p>';

    main.innerHTML = `
      <div class="detailType">
        <span class="typeTag">${item.category}</span>
        <span class="badge ${competitionStatusClass(item.status)}">${item.status}</span>
      </div>

      <h1 class="detailTitle">${item.title}</h1>

      <div class="detailMeta">
        <span>${icons.clock} ${item.startDate} ~ ${item.endDate}</span>
        <span class="sepDot"></span>
        <span>${icons.eye} ${item.participants} 人参与</span>
      </div>

      <div class="detailBody">
        <p>${item.description}</p>
        ${tagsHtml ? `<div class="compTags">${tagsHtml}</div>` : ''}

        <h3>赛程安排</h3>
        <div class="timeline">${roundsHtml}</div>

        <h3>竞赛规则</h3>
        <ul class="ruleList">${rulesHtml}</ul>

        <h3>评审标准</h3>
        <ul class="ruleList">${criteriaHtml}</ul>

        <h3>评分权重</h3>
        <div class="weightList">${weightsHtml}</div>

        <h3>奖项设置</h3>
        <div class="awardRow">
          <div class="awardItem gold">${icons.coin}<span class="awardCount">${item.goldMedals}</span><span>金奖</span></div>
          <div class="awardItem silver">${icons.coin}<span class="awardCount">${item.silverMedals}</span><span>银奖</span></div>
          <div class="awardItem bronze">${icons.coin}<span class="awardCount">${item.bronzeMedals}</span><span>铜奖</span></div>
        </div>

        <h3>排行榜</h3>
        ${leaderboardHtml}
      </div>`;
  }

  if (side) {
    const registerAction =
      item.status === '已结束'
        ? '<span class="badge badgeGrey" style="padding:10px 16px;font-size:14px;width:100%;text-align:center">比赛已结束</span>'
        : `<a class="btn btnPrimary" style="width:100%;margin-top:12px" href="#" onclick="showToast('报名成功（原型演示）','success');return false;">立即报名</a>`;

    side.innerHTML = `
      <div class="sideCard">
        <div class="sideCardTitle">赛事信息</div>
        <div class="rewardHero">
          <div class="rewardMain">${icons.coin} ${item.reward}</div>
          <div class="rewardSub">总奖金</div>
        </div>
        <div class="statRow"><span class="k">参与人数</span><span class="v">${item.participants}</span></div>
        <div class="statRow"><span class="k">提交数量</span><span class="v">${item.submissions}</span></div>
        <div class="statRow"><span class="k">团队规模</span><span class="v">${item.teamSizeMin}-${item.teamSizeMax} 人</span></div>
        ${registerAction}
      </div>
      <div class="sideCard">
        <div class="sideCardTitle">关键时间</div>
        <div class="statRow"><span class="k">报名开始</span><span class="v">${item.registrationStart}</span></div>
        <div class="statRow"><span class="k">报名截止</span><span class="v">${item.registrationEnd}</span></div>
        <div class="statRow"><span class="k">提交截止</span><span class="v">${item.submissionEnd}</span></div>
        <div class="statRow"><span class="k">结果公布</span><span class="v">${item.resultPublishDate}</span></div>
      </div>`;
  }
}

/* ==========================================================================
   议题详情页逻辑
   ========================================================================== */

/**
 * 议题优先级徽章样式映射
 * @param {string} priority - 优先级：high / medium / low
 * @returns {string} badge 样式类名
 */
function issuePriorityClass(priority) {
  return { high: 'badgeOrange', medium: 'badgeBlue', low: 'badgeGrey' }[priority] || 'badgeGrey';
}

/**
 * 议题优先级文案映射
 * @param {string} priority - 优先级：high / medium / low
 * @returns {string} 优先级中文文案
 */
function issuePriorityLabel(priority) {
  return { high: '高', medium: '中', low: '低' }[priority] || '—';
}

/**
 * 议题标签颜色样式映射
 * @param {string} color - 标签颜色
 * @returns {string} badge 样式类名
 */
function labelColorClass(color) {
  return {
    blue: 'badgeBlue',
    green: 'badgeGreen',
    red: 'badgeRed',
    orange: 'badgeOrange',
    purple: 'badgePurple'
  }[color] || 'badgeGrey';
}

/**
 * 初始化议题详情页：渲染导航/页脚，并按 URL id 渲染详情
 */
function initIssueDetailPage() {
  renderNavbar('issues');
  renderFooter();

  const params = new URLSearchParams(window.location.search);
  const id = params.get('id') || 'i1';
  const item = issuesData.find((i) => i.id === id) || issuesData[0];

  renderIssueDetail(item);
}

/**
 * 渲染议题详情页主体与侧边栏
 * @param {Object} item - 议题数据对象
 */
function renderIssueDetail(item) {
  const main = document.getElementById('issueMain');
  const side = document.getElementById('issueSide');

  const typeClass = { 建议: 'badgeBlue', 问题: 'badgeOrange', 议题: 'badgePurple' }[item.type] || 'badgeGrey';

  if (main) {
    const labelsHtml = (item.labels || [])
      .map((l) => `<span class="badge ${labelColorClass(l.color)}">${l.name}</span>`)
      .join('');

    const commentsHtml = (item.issueComments || [])
      .map(
        (c) => `
        <div class="commentItem">
          <span class="avatar" ${c.isAI ? 'style="background:linear-gradient(135deg,#9b6bff,#c9a8ff)"' : ''}>${c.authorInitial}</span>
          <div class="commentBody">
            <div class="commentHead">
              <span class="commentAuthor">${c.author}</span>
              ${c.isAI ? '<span class="badge badgePurple">官方</span>' : ''}
              <span class="commentTime">${c.time}</span>
            </div>
            <p class="commentText">${c.text}</p>
          </div>
        </div>`
      )
      .join('');

    main.innerHTML = `
      <div class="detailType">
        <span class="badge ${typeClass}">${item.type}</span>
        <span class="badge ${item.status === '开放' ? 'badgeGreen' : 'badgeGrey'}">${item.status}</span>
        <span class="badge ${issuePriorityClass(item.priority)}">优先级 ${issuePriorityLabel(item.priority)}</span>
        ${item.isPinned ? '<span class="badge badgeOrange">已置顶</span>' : ''}
        ${item.isAiCompleted ? '<span class="badge badgePurple">AI 已处理</span>' : ''}
      </div>

      <h1 class="detailTitle">${item.title}</h1>

      <div class="cardAuthor" style="margin-bottom:12px">
        <span class="avatar">${item.authorInitial}</span>
        <span class="cardAuthorName">${item.author}</span>
        <span class="issueAuthorTime">${item.time}</span>
      </div>

      <div class="issueDescBox">
        <p>${item.description}</p>
      </div>

      ${labelsHtml ? `<div class="labelList">${labelsHtml}</div>` : ''}

      <div class="commentSection">
        <h3 style="font-size:18px;font-weight:700;margin-bottom:12px">评论 · ${(item.issueComments || []).length}</h3>
        <div class="commentInput">
          <span class="avatar" style="width:36px;height:36px;font-size:13px">我</span>
          <textarea id="issueCommentInput" placeholder="写下你的评论，参与讨论…"></textarea>
        </div>
        <div style="text-align:right;margin-bottom:16px">
          <button class="btn btnPrimary" id="issueCommentBtn">发表评论</button>
        </div>
        ${commentsHtml}
      </div>`;
  }

  if (side) {
    const assigneesHtml = (item.assignees || []).length
      ? (item.assignees || [])
          .map(
            (a) => `
          <div class="assigneeItem">
            <span class="avatar">${a.initial}</span>
            <span class="assigneeName">${a.name}</span>
          </div>`
          )
          .join('')
      : '<p class="emptyHint">暂无指派人</p>';

    side.innerHTML = `
      <div class="sideCard">
        <div class="sideCardTitle">指派人</div>
        ${assigneesHtml}
      </div>
      <div class="sideCard">
        <div class="sideCardTitle">议题信息</div>
        <div class="statRow"><span class="k">提交人</span><span class="v">${item.author}</span></div>
        <div class="statRow"><span class="k">类型</span><span class="v">${item.type}</span></div>
        <div class="statRow"><span class="k">状态</span><span class="v">${item.status}</span></div>
        <div class="statRow"><span class="k">优先级</span><span class="v">${issuePriorityLabel(item.priority)}</span></div>
        <div class="statRow"><span class="k">提交时间</span><span class="v">${item.time}</span></div>
      </div>`;
  }

  // 评论提交交互
  const commentBtn = document.getElementById('issueCommentBtn');
  if (commentBtn) {
    commentBtn.addEventListener('click', () => {
      const textarea = document.getElementById('issueCommentInput');
      if (textarea && textarea.value.trim()) {
        showToast('评论已提交（原型演示）', 'success');
        textarea.value = '';
      } else {
        showToast('请输入评论内容', 'error');
      }
    });
  }
}
