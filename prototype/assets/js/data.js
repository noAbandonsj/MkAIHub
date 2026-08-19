/**
 * DataClawHub 前台原型 · Mock 数据源
 * 全部数据仅用于高保真原型演示，后续由后端 API 替换
 */

/**
 * 导航菜单数据
 * @property {string} label - 显示文字
 * @property {string} href - 链接地址
 * @property {boolean} hasDropdown - 是否有下拉子菜单
 * @property {string} key - 唯一标识（用于高亮）
 * @property {Array} [children] - 下拉子菜单项（可选）
 */
const navItems = [
  {
    label: '探索',
    href: 'featured.html',
    hasDropdown: true,
    key: 'featured',
    children: [
      { label: '精选任务', href: 'featured.html' },
      { label: '知识库', href: 'knowledge.html' }
    ]
  },
  { label: '任务', href: 'tasks.html', hasDropdown: false, key: 'tasks' },
  { label: '展品', href: 'artifacts.html', hasDropdown: true, key: 'artifacts' },
  { label: '知识库', href: 'knowledge.html', hasDropdown: false, key: 'knowledge' },
  { label: 'Issues', href: 'issues.html', hasDropdown: false, key: 'issues' },
  { label: '竞赛', href: 'competitions.html', hasDropdown: true, key: 'competitions' }
];

/**
 * 制品数据集合（物流 AI 领域）
 * @property {string} id - 唯一标识
 * @property {string} type - 类型：场景Demo / dataset / report / source
 * @property {string} title - 标题
 * @property {string} author - 作者名
 * @property {string} authorInitial - 头像首字母
 * @property {string} description - 描述
 * @property {string} fileName - 主文件名
 * @property {string} fileSize - 文件大小
 * @property {number} fileCount - 文件数量
 * @property {string} permission - 权限：人工 / AI
 * @property {string} visibility - 可见性：公开 / 私有
 * @property {boolean} verified - 是否已验证
 * @property {string} tool - 执行工具
 * @property {string} model - 大模型
 * @property {string} duration - 耗时
 * @property {string} tokens - Token 消耗
 * @property {string} cost - 预估费用
 * @property {string} domain - 领域
 * @property {string} industry - 行业
 * @property {number} downloads - 下载量
 * @property {number} likes - 点赞数
 */
const artifacts = [
  {
    id: 'a1',
    type: '场景Demo',
    title: '多式联运路线可视化大屏',
    author: '陈启航',
    authorInitial: '陈',
    description: '基于真实港口与铁路数据，构建公铁水多式联运路线的三维可视化大屏，支持实时运力调度与碳排放模拟。',
    fileName: 'multimodal-demo.zip',
    fileSize: '24.6 MB',
    fileCount: 3,
    permission: '人工',
    visibility: '公开',
    verified: true,
    tool: 'ECharts',
    model: 'DeepSeek-V3',
    duration: '1h 12m',
    tokens: '18.4k',
    cost: '¥0.86',
    domain: '运输规划',
    industry: '多式联运',
    downloads: 342,
    likes: 128
  },
  {
    id: 'a2',
    type: 'dataset',
    title: '华东生鲜冷链仓储温控数据集',
    author: '林晓雅',
    authorInitial: '林',
    description: '覆盖华东 6 个冷链仓、连续 90 天的温湿度与库存周转数据，含异常标注，可直接用于冷链异常检测模型训练。',
    fileName: 'coldchain-ehu-dataset.csv',
    fileSize: '312 MB',
    fileCount: 12,
    permission: 'AI',
    visibility: '公开',
    verified: true,
    tool: 'Pandas',
    model: 'GLM-4',
    duration: '32m',
    tokens: '6.2k',
    cost: '¥0.31',
    domain: '仓储管理',
    industry: '冷链物流',
    downloads: 519,
    likes: 204
  },
  {
    id: 'a3',
    type: 'report',
    title: '港口集装箱调度算法研究报告',
    author: '王海峰',
    authorInitial: '王',
    description: '系统梳理港口岸桥与堆场调度的主流优化算法，对比遗传算法、强化学习在真实吞吐场景下的性能表现。',
    fileName: 'port-scheduling-report.pdf',
    fileSize: '8.4 MB',
    fileCount: 1,
    permission: '人工',
    visibility: '公开',
    verified: false,
    tool: 'Markdown',
    model: 'Qwen2.5',
    duration: '48m',
    tokens: '11.7k',
    cost: '¥0.52',
    domain: '港口调度',
    industry: '海运物流',
    downloads: 187,
    likes: 76
  },
  {
    id: 'a4',
    type: 'source',
    title: '配送路径规划开源算法库',
    author: '赵一鸣',
    authorInitial: '赵',
    description: '面向末端配送的 VRP 求解器，内置禁忌搜索与自适应大邻域搜索，支持 50-500 节点的路径优化。',
    fileName: 'route-optimizer-py.zip',
    fileSize: '1.8 MB',
    fileCount: 24,
    permission: '人工',
    visibility: '公开',
    verified: true,
    tool: 'Python',
    model: '—',
    duration: '—',
    tokens: '—',
    cost: '免费',
    domain: '路径优化',
    industry: '城市配送',
    downloads: 623,
    likes: 245
  },
  {
    id: 'a5',
    type: 'dataset',
    title: '供应链需求预测基准数据集',
    author: '周雨桐',
    authorInitial: '周',
    description: '含 3 年 SKU 级销售与补货记录，覆盖快消、3C 与医药三类供应链，提供多时间粒度聚合版本。',
    fileName: 'demand-forecast-bench.zip',
    fileSize: '156 MB',
    fileCount: 8,
    permission: 'AI',
    visibility: '公开',
    verified: true,
    tool: 'Pandas',
    model: 'GPT-4o',
    duration: '1h 05m',
    tokens: '15.9k',
    cost: '¥0.73',
    domain: '需求预测',
    industry: '供应链',
    downloads: 438,
    likes: 167
  },
  {
    id: 'a6',
    type: '场景Demo',
    title: '智能仓储 AGV 协同调度演示',
    author: '郑博文',
    authorInitial: '郑',
    description: '模拟 30 台 AGV 在智能仓内的避障与任务分配，可视化展示基于多智能体强化学习的调度效果。',
    fileName: 'agv-scheduling-demo.zip',
    fileSize: '18.2 MB',
    fileCount: 2,
    permission: '人工',
    visibility: '公开',
    verified: false,
    tool: 'Python',
    model: 'DeepSeek-V3',
    duration: '55m',
    tokens: '13.3k',
    cost: '¥0.61',
    domain: '仓储管理',
    industry: '智能仓储',
    downloads: 276,
    likes: 98
  },
  {
    id: 'a7',
    type: 'source',
    title: '运输碳排放核算与碳积分工具',
    author: '孙浩然',
    authorInitial: '孙',
    description: '依据温室气体协议核算运输环节碳排，支持公路、铁路、水运多模式，输出可审计的碳积分报告。',
    fileName: 'carbon-accounting-js.zip',
    fileSize: '0.9 MB',
    fileCount: 16,
    permission: '人工',
    visibility: '私有',
    verified: false,
    tool: 'TypeScript',
    model: '—',
    duration: '—',
    tokens: '—',
    cost: '免费',
    domain: '绿色物流',
    industry: '碳排放',
    downloads: 0,
    likes: 34
  },
  {
    id: 'a8',
    type: 'report',
    title: '快递分拣中心效率分析报告',
    author: '钱思远',
    authorInitial: '钱',
    description: '基于某头部快递企业 12 个分拣中心的实测数据，分析自动分拣线瓶颈并提出改造 ROI 测算。',
    fileName: 'sorting-center-analysis.pdf',
    fileSize: '12.7 MB',
    fileCount: 2,
    permission: 'AI',
    visibility: '公开',
    verified: true,
    tool: 'Markdown',
    model: 'Claude 3.5',
    duration: '1h 40m',
    tokens: '22.1k',
    cost: '¥1.04',
    domain: '分拣中心',
    industry: '快递物流',
    downloads: 205,
    likes: 82
  }
];

/**
 * 制品 AI 评价数据（对齐源网站 /api/artifacts 的 ai_evaluation 结构）
 * @property {string} status - 评价状态：completed / pending
 * @property {number} overallScore - 综合评分（0-100）
 * @property {Object} dimensions - 四维评分：需求符合度 / 完整性 / 质量 / 可用性
 * @property {string} summary - AI 评价总结
 */
const artifactEvaluations = {
  a1: {
    status: 'completed',
    overallScore: 93,
    dimensions: {
      requirementFit: { score: 95, comment: '完全满足场景 Demo 任务要求。demo 内联所有样式与脚本，CSS 中明确定义公铁水多运输方式配色，JS 通过相机交互实现地图拖拽、缩放与高亮，路线卡片与过滤器支持展示多条典型多式联运路线，桌面端布局稳定。' },
      completeness: { score: 90, comment: '包含顶部统计面板、左侧路线列表、中央 SVG 交互地图、底部时效对比与节点详情等全部核心要素，首尾结构完整，未遗漏验收标准要求的输出项。' },
      quality: { score: 92, comment: '采用现代 CSS 变量与 Grid/Flex 布局，暗色主题视觉层级清晰，交互状态设计专业；JS 为纯原生实现、状态驱动、渲染模块化，坐标换算严谨，可读性与可维护性俱佳。' },
      usability: { score: 95, comment: '开箱即用，无需构建步骤或本地服务器，双击即可在主流桌面浏览器运行，地图缩放、路线切换、时效对比均可正常操作，直接满足演示与交付要求。' }
    },
    summary: '该制品高质量完成了多式联运路线可视化大屏的开发。单文件零依赖架构确保了极高的可用性与兼容性，交互地图、多运输方式标识与时效对比图表均严格对齐验收标准，代码结构清晰、视觉专业，可直接用于业务演示。'
  },
  a2: {
    status: 'completed',
    overallScore: 91,
    dimensions: {
      requirementFit: { score: 92, comment: '精准覆盖华东 6 个冷链仓、连续 90 天的温湿度与库存周转数据，异常标注粒度适配冷链异常检测建模，范围与维度均贴合需求。' },
      completeness: { score: 90, comment: '数据集字段设计完整，含时间戳、温区、湿度、周转量与异常标签，提供 12 个分文件与统一索引，无关键字段遗漏。' },
      quality: { score: 90, comment: '数据清洗规范，异常值标注依据清晰，采样率与缺失率透明披露，可直接支撑监督学习模型的训练与验证。' },
      usability: { score: 92, comment: 'CSV 编码规范、分隔符统一、无乱码，可直接导入 Pandas 或 BI 工具进行二次分析，开箱即用。' }
    },
    summary: '该数据集质量优秀，覆盖范围精准、字段完整、清洗规范，异常标注清晰可靠，可直接用于冷链异常检测模型的训练与复现，具备较高的业务参考价值。'
  },
  a3: {
    status: 'completed',
    overallScore: 88,
    dimensions: {
      requirementFit: { score: 88, comment: '系统梳理了港口岸桥与堆场调度的主流优化算法，并针对遗传算法与强化学习在真实吞吐场景下的表现进行了对比，契合研究报告主题。' },
      completeness: { score: 86, comment: '报告结构完整，涵盖问题建模、算法原理、仿真实验与结论建议，但部分对比实验缺少统一的基准数据集说明。' },
      quality: { score: 90, comment: '文献引用规范，术语使用准确，算法复杂度与收敛性分析严谨，图表清晰，专业性强。' },
      usability: { score: 86, comment: 'PDF 排版规整，可直接用于内部技术评审或作为算法选型参考，缺少可复现的实验代码配套。' }
    },
    summary: '该研究报告内容专业、逻辑严密，对主流港口调度算法的对比分析具备工程参考价值。不足在于实验基准说明与可复现代码的缺失，但仍可作为算法选型与方案评审的重要依据。'
  },
  a4: {
    status: 'completed',
    overallScore: 94,
    dimensions: {
      requirementFit: { score: 95, comment: '完全满足配送路径规划需求，内置禁忌搜索与自适应大邻域搜索，支持 50-500 节点的 VRP 求解，覆盖规模区间精准。' },
      completeness: { score: 92, comment: '算法库结构完整，含求解器核心、算例生成器、结果可视化与 24 个源文件，功能模块无遗漏。' },
      quality: { score: 94, comment: '代码工程化程度高，模块解耦清晰，算法实现严谨，注释率充分，具备良好的可扩展性与可维护性。' },
      usability: { score: 95, comment: '提供清晰的 CLI 与 Python API 双入口，依赖安装指引完善，可直接集成到末端配送调度系统，开箱即用。' }
    },
    summary: '该开源算法库质量优秀，算法实现严谨、工程化程度高，覆盖中大规模 VRP 场景，可直接用于生产环境的末端配送路径优化，具备极高的实用价值。'
  },
  a5: {
    status: 'completed',
    overallScore: 90,
    dimensions: {
      requirementFit: { score: 90, comment: '覆盖快消、3C 与医药三类供应链的 SKU 级销售与补货记录，支持多时间粒度聚合，契合需求预测基准的定位。' },
      completeness: { score: 89, comment: '含 3 年历史数据与补货记录，字段覆盖 SKU、销量、库存、补货量与时间戳，提供多粒度聚合版本，结构完整。' },
      quality: { score: 90, comment: '数据口径统一，时间序列连续性好，异常值已标识，适合作为时序预测模型的标准化基准。' },
      usability: { score: 91, comment: '数据以压缩包交付并附字段说明，解压后可直接用于模型训练，兼容主流时序预测框架。' }
    },
    summary: '该数据集覆盖全面、口径统一、质量可靠，为供应链需求预测提供了标准化的建模基准，可直接服务于快消、3C 与医药等多行业的预测任务。'
  },
  a6: {
    status: 'completed',
    overallScore: 87,
    dimensions: {
      requirementFit: { score: 88, comment: '模拟 30 台 AGV 在智能仓内的避障与任务分配，可视化呈现多智能体强化学习调度效果，契合场景演示定位。' },
      completeness: { score: 86, comment: '包含 AGV 避障、任务分配与调度可视化核心功能，但缺少极端拥堵场景的边界处理说明。' },
      quality: { score: 87, comment: '强化学习调度逻辑实现清晰，可视化动画流畅，代码结构合理，部分参数缺少注释说明。' },
      usability: { score: 87, comment: '可通过脚本一键启动演示，运行依赖已封装，便于向业务方快速展示调度效果。' }
    },
    summary: '该场景 Demo 完整呈现了多智能体强化学习下的 AGV 协同调度效果，动画直观、逻辑清晰，可作为智能仓储方案演示与效果验证的参考。'
  },
  a7: {
    status: 'completed',
    overallScore: 85,
    dimensions: {
      requirementFit: { score: 86, comment: '依据温室气体协议核算运输环节碳排，支持公路、铁路、水运多模式，并输出可审计碳积分报告，契合绿色物流需求。' },
      completeness: { score: 84, comment: '核算工具覆盖多运输模式与报告输出，但碳因子库覆盖范围有限，缺少部分细分运输方式的排放因子。' },
      quality: { score: 86, comment: '核算逻辑依据标准协议实现，计算口径清晰，报告格式可审计，代码类型安全、结构清晰。' },
      usability: { score: 84, comment: '提供 JS 库形式便于集成，但需自行配置碳因子数据源，私有制品需权限申请后方可下载。' }
    },
    summary: '该工具核算逻辑规范、输出可审计，覆盖多运输模式碳排计算，可支撑企业绿色物流碳积分管理，后续可扩展碳因子库以提升覆盖度。'
  },
  a8: {
    status: 'completed',
    overallScore: 89,
    dimensions: {
      requirementFit: { score: 90, comment: '基于 12 个分拣中心实测数据分析自动分拣线瓶颈并提出改造 ROI 测算，精准契合效率分析主题。' },
      completeness: { score: 88, comment: '报告覆盖数据采集、瓶颈识别、改造方案与 ROI 测算全链路，分析维度完整。' },
      quality: { score: 89, comment: '数据来源真实可追溯，瓶颈分析方法科学，ROI 测算模型清晰，图表表达规范。' },
      usability: { score: 88, comment: 'PDF 可直接用于管理层决策汇报，结论与建议具备实操指导价值。' }
    },
    summary: '该分析报告数据扎实、方法科学，对分拣中心瓶颈的定位与 ROI 测算准确可信，可作为快递企业分拣环节提质增效的决策依据。'
  }
};

/**
 * 首页精选制品（引用 artifacts 的子集）
 */
const featuredIds = ['a1', 'a4', 'a2', 'a5'];

/**
 * 制品详情评论数据
 */
const comments = [
  {
    author: '刘子墨',
    authorInitial: '刘',
    time: '2 小时前',
    text: '数据集质量很高，异常标注的粒度正好适合我们的异常检测模型，已成功复现实验。'
  },
  {
    author: '吴佳琪',
    authorInitial: '吴',
    time: '昨天 18:32',
    text: '多式联运的碳排放模拟很实用，希望能补充海运段的排放因子说明。'
  },
  {
    author: 'AI 助手',
    authorInitial: 'AI',
    time: '昨天 09:15',
    text: '该制品已通过自动化审核：文件格式合规、无恶意脚本、许可证信息完整。',
    isAI: true
  }
];

/**
 * 制品库筛选维度选项数据（对齐源网站 /artifacts 页）
 * @property {Array} searchModes - 搜索模式标签（含图标与占位提示）
 * @property {Array} formats - 格式选择（源网站支持 10 种文件格式）
 * @property {Array} sortBy - 排序方式
 */
const filterOptions = {
  // 搜索模式标签（关键词/领域/行业/上传者，带图标与占位提示）
  searchModes: [
    { key: 'keyword', emoji: '🔍', label: '关键词', placeholder: '输入关键词搜索制品文件名...' },
    { key: 'domain', emoji: '🗺️', label: '领域', placeholder: '输入领域名称筛选制品...' },
    { key: 'industry', emoji: '🏭', label: '行业', placeholder: '输入行业名称筛选制品...' },
    { key: 'uploader', emoji: '👤', label: '上传者', placeholder: '输入上传者名称筛选制品...' }
  ],
  // 格式选择
  formats: ['全部', 'CSV', 'XLSX', 'XLS', 'JSON', 'DB', 'TXT', 'PPT', 'PPTX', 'ZIP', 'RAR'],
  // 排序方式
  sortBy: ['最新', '下载最多', '点赞最多']
};

/**
 * 竞赛活动数据（物流 AI 竞赛，对齐源网站 /api/competitions 详情结构）
 * @property {string} id - 唯一标识
 * @property {string} title - 竞赛标题
 * @property {string} description - 竞赛描述
 * @property {string} status - 状态：即将开始 / 报名中 / 进行中 / 已结束
 * @property {string} category - 领域分类：港航/口岸/公路/铁路/航空/特种运输/多式联运/物流枢纽/其他
 * @property {string} startDate - 比赛开始日期
 * @property {string} endDate - 比赛结束日期
 * @property {string} reward - 奖金
 * @property {number} participants - 参与人数
 * @property {boolean} followed - 当前用户是否已关注（演示「已关注」筛选）
 * @property {boolean} registered - 当前用户是否已报名（演示「已报名」筛选）
 * @property {string} registrationStart - 报名开始
 * @property {string} registrationEnd - 报名截止
 * @property {string} submissionEnd - 提交截止
 * @property {string} resultPublishDate - 结果公布
 * @property {number} teamSizeMin - 最小团队人数
 * @property {number} teamSizeMax - 最大团队人数
 * @property {number} goldMedals - 金奖数量
 * @property {number} silverMedals - 银奖数量
 * @property {number} bronzeMedals - 铜奖数量
 * @property {Array} tags - 标签
 * @property {Array} rules - 竞赛规则
 * @property {Array} evaluationCriteria - 评审标准
 * @property {Array} scoringWeights - 评分权重
 * @property {Array} rounds - 赛程阶段
 * @property {number} submissions - 提交数
 * @property {Array} leaderboard - 排行榜（可选）
 */
const competitions = [
  {
    id: 'c1',
    title: '第三届物流路径优化挑战赛',
    description: '基于真实城市配送网络，优化多约束下的末端配送路径，目标最小化总里程与超时率。',
    status: '进行中',
    category: '公路',
    startDate: '2026-07-01',
    endDate: '2026-09-30',
    reward: '¥50,000',
    participants: 486,
    followed: true,
    registered: true,
    registrationStart: '2026-06-01',
    registrationEnd: '2026-06-30',
    submissionEnd: '2026-09-20',
    resultPublishDate: '2026-09-30',
    teamSizeMin: 1,
    teamSizeMax: 5,
    goldMedals: 1,
    silverMedals: 3,
    bronzeMedals: 6,
    tags: ['路径优化', '城市配送', '运筹优化'],
    rules: [
      '参赛者可使用任意编程语言，但提交的求解器须能在标准容器环境中复现',
      '禁止在评测数据上人工标注或过拟合，一经发现取消成绩',
      '每个团队最多提交 5 次，取评测集上的最优成绩'
    ],
    evaluationCriteria: [
      '总里程与超时率的加权目标值越低越好',
      '求解稳定性：多次运行结果方差需在可接受范围内',
      '代码规范性与可复现性'
    ],
    scoringWeights: [
      { name: '算法性能', weight: 50 },
      { name: '创新性', weight: 20 },
      { name: '可落地性', weight: 30 }
    ],
    rounds: [
      { name: '初赛', date: '2026-07-01 ~ 2026-08-15', desc: '开放评测 A 榜，线上自动评测实时更新排名' },
      { name: '复赛', date: '2026-08-16 ~ 2026-09-15', desc: '切换评测 B 榜，提交技术报告进入综合评审' },
      { name: '决赛答辩', date: '2026-09-20 ~ 2026-09-30', desc: '线上答辩与代码核验，公布最终奖项' }
    ],
    submissions: 156,
    leaderboard: [
      { rank: 1, team: '极速派送队', score: 98.6 },
      { rank: 2, team: '运筹帷幄', score: 97.2 },
      { rank: 3, team: 'RouteMaster', score: 96.8 },
      { rank: 4, team: '最后一公里', score: 95.4 },
      { rank: 5, team: '动态规划组', score: 94.1 }
    ]
  },
  {
    id: 'c2',
    title: '冷链温度异常检测算法大赛',
    description: '利用多传感器时序数据，检测冷链运输过程中的温度异常事件，追求精度与召回平衡。',
    status: '报名中',
    category: '特种运输',
    startDate: '2026-08-20',
    endDate: '2026-10-20',
    reward: '¥30,000',
    participants: 213,
    followed: false,
    registered: false,
    registrationStart: '2026-07-20',
    registrationEnd: '2026-08-19',
    submissionEnd: '2026-10-15',
    resultPublishDate: '2026-10-20',
    teamSizeMin: 1,
    teamSizeMax: 3,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 5,
    tags: ['异常检测', '时序分析', '冷链'],
    rules: [
      '仅使用官方提供的传感器时序数据，禁止引入外部数据',
      '评测以事件级 F1 与检测延迟综合评分',
      '每个团队每日最多提交 2 次'
    ],
    evaluationCriteria: [
      '温度异常事件检测的准确率与召回率',
      '模型推理延迟与资源占用',
      '异常解释能力与工程完整度'
    ],
    scoringWeights: [
      { name: '检测性能', weight: 60 },
      { name: '推理效率', weight: 20 },
      { name: '可解释性', weight: 20 }
    ],
    rounds: [
      { name: '初赛', date: '2026-08-20 ~ 2026-09-20', desc: '线上评测实时排名，取前 60% 晋级复赛' },
      { name: '复赛', date: '2026-09-21 ~ 2026-10-15', desc: '隐藏测试集评测，提交技术报告' }
    ],
    submissions: 0,
    leaderboard: []
  },
  {
    id: 'c3',
    title: '港口集装箱吞吐量预测赛',
    description: '融合历史吞吐、船舶到港与天气数据，预测未来 7 天港口集装箱吞吐量。',
    status: '已结束',
    category: '港航',
    startDate: '2026-03-01',
    endDate: '2026-05-31',
    reward: '¥20,000',
    participants: 358,
    followed: false,
    registered: false,
    registrationStart: '2026-02-01',
    registrationEnd: '2026-02-28',
    submissionEnd: '2026-05-25',
    resultPublishDate: '2026-05-31',
    teamSizeMin: 1,
    teamSizeMax: 3,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 4,
    tags: ['吞吐预测', '时序预测', '港航'],
    rules: [
      '使用官方提供的港航业务数据，可引入公开天气数据',
      '预测目标为未来 7 天逐日集装箱吞吐量',
      '以加权 MAPE 为最终评测指标'
    ],
    evaluationCriteria: [
      '预测精度（加权 MAPE）',
      '模型泛化能力与鲁棒性',
      '特征工程与方案创新'
    ],
    scoringWeights: [
      { name: '预测精度', weight: 70 },
      { name: '鲁棒性', weight: 15 },
      { name: '创新性', weight: 15 }
    ],
    rounds: [
      { name: '初赛', date: '2026-03-01 ~ 2026-04-20', desc: '线上 A/B 榜评测' },
      { name: '复赛', date: '2026-04-21 ~ 2026-05-25', desc: '隐藏测试集与报告评审' }
    ],
    submissions: 198,
    leaderboard: [
      { rank: 1, team: '港航预测组', score: 92.3 },
      { rank: 2, team: 'TideFlow', score: 91.5 },
      { rank: 3, team: '深蓝时序', score: 90.7 },
      { rank: 4, team: 'ForecastAI', score: 89.2 },
      { rank: 5, team: '港口之星', score: 88.6 }
    ]
  },
  {
    id: 'c4',
    title: '智能仓储 AGV 协同调度赛',
    description: '在动态订单环境下，为 30 台 AGV 规划无碰撞的最优调度策略，最小化平均履约时长。',
    status: '进行中',
    category: '物流枢纽',
    startDate: '2026-06-15',
    endDate: '2026-08-31',
    reward: '¥40,000',
    participants: 271,
    followed: true,
    registered: false,
    registrationStart: '2026-05-15',
    registrationEnd: '2026-06-14',
    submissionEnd: '2026-08-25',
    resultPublishDate: '2026-08-31',
    teamSizeMin: 2,
    teamSizeMax: 4,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 5,
    tags: ['AGV调度', '多智能体', '仓储'],
    rules: [
      '基于官方仿真环境进行策略开发，禁止修改仿真底层逻辑',
      '以多场景平均履约时长为核心指标',
      '每个团队每周最多提交 10 次'
    ],
    evaluationCriteria: [
      '平均履约时长与任务完成率',
      '无碰撞与死锁处理能力',
      '策略在未见场景上的泛化表现'
    ],
    scoringWeights: [
      { name: '履约效率', weight: 55 },
      { name: '安全无碰撞', weight: 25 },
      { name: '泛化能力', weight: 20 }
    ],
    rounds: [
      { name: '初赛', date: '2026-06-15 ~ 2026-07-31', desc: '标准场景线上评测' },
      { name: '复赛', date: '2026-08-01 ~ 2026-08-25', desc: '随机场景与答辩评审' }
    ],
    submissions: 121,
    leaderboard: [
      { rank: 1, team: '蜂群调度', score: 96.1 },
      { rank: 2, team: 'AGV-Master', score: 95.3 },
      { rank: 3, team: '仓内无界', score: 93.9 },
      { rank: 4, team: '多智体实验室', score: 92.7 },
      { rank: 5, team: '库内极速', score: 91.5 }
    ]
  },
  {
    id: 'c5',
    title: '多式联运枢纽网络规划赛',
    description: '围绕公铁水多式联运枢纽，规划转运网络与运力分配，压缩整体运输成本与时效。',
    status: '即将开始',
    category: '多式联运',
    startDate: '2026-09-10',
    endDate: '2026-11-10',
    reward: '¥60,000',
    participants: 0,
    followed: false,
    registered: false,
    registrationStart: '2026-08-10',
    registrationEnd: '2026-09-09',
    submissionEnd: '2026-11-05',
    resultPublishDate: '2026-11-10',
    teamSizeMin: 1,
    teamSizeMax: 6,
    goldMedals: 1,
    silverMedals: 3,
    bronzeMedals: 8,
    tags: ['多式联运', '网络规划', '运力分配'],
    rules: [
      '使用官方提供的枢纽与运力数据，可补充公开地理数据',
      '以综合成本与时效加权目标作为评测指标',
      '提交方案须可解释并附完整求解流程'
    ],
    evaluationCriteria: [
      '运输成本与时效的综合优化效果',
      '网络方案的可行性与鲁棒性',
      '求解方法的创新性与效率'
    ],
    scoringWeights: [
      { name: '成本时效', weight: 60 },
      { name: '方案可行性', weight: 20 },
      { name: '方法创新', weight: 20 }
    ],
    rounds: [
      { name: '报名阶段', date: '2026-08-10 ~ 2026-09-09', desc: '开放报名与数据集下载' },
      { name: '初赛', date: '2026-09-10 ~ 2026-10-20', desc: '线上评测 A 榜' },
      { name: '复赛', date: '2026-10-21 ~ 2026-11-05', desc: '隐藏测试集与综合评审' }
    ],
    submissions: 0,
    leaderboard: []
  },
  {
    id: 'c6',
    title: '航空货运装载优化赛',
    description: '针对窄体/宽体货机舱位装载，优化货物配载与重心约束，提升单机装载率。',
    status: '报名中',
    category: '航空',
    startDate: '2026-08-25',
    endDate: '2026-10-25',
    reward: '¥35,000',
    participants: 164,
    followed: false,
    registered: false,
    registrationStart: '2026-07-25',
    registrationEnd: '2026-08-24',
    submissionEnd: '2026-10-20',
    resultPublishDate: '2026-10-25',
    teamSizeMin: 1,
    teamSizeMax: 4,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 5,
    tags: ['航空货运', '装载优化', '配载'],
    rules: [
      '使用官方提供的机型与货物数据，须满足重心与限载约束',
      '以装载率与配载时间综合评分',
      '禁止使用暴力枚举以外的人工干预'
    ],
    evaluationCriteria: [
      '单机装载率与空间利用率',
      '重心约束满足度与配载稳定性',
      '求解速度与工程可用性'
    ],
    scoringWeights: [
      { name: '装载率', weight: 50 },
      { name: '配载安全', weight: 30 },
      { name: '求解效率', weight: 20 }
    ],
    rounds: [
      { name: '初赛', date: '2026-08-25 ~ 2026-09-25', desc: '标准机型线上评测' },
      { name: '复赛', date: '2026-09-26 ~ 2026-10-20', desc: '多机型混合场景评测' }
    ],
    submissions: 0,
    leaderboard: []
  },
  {
    id: 'c7',
    title: '口岸通关效率预测赛',
    description: '基于口岸报关、查验与放行数据，预测通关时长并识别效率瓶颈。',
    status: '报名中',
    category: '口岸',
    startDate: '2026-08-18',
    endDate: '2026-10-18',
    reward: '¥25,000',
    participants: 132,
    followed: false,
    registered: true,
    registrationStart: '2026-07-18',
    registrationEnd: '2026-08-17',
    submissionEnd: '2026-10-13',
    resultPublishDate: '2026-10-18',
    teamSizeMin: 1,
    teamSizeMax: 3,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 4,
    tags: ['通关预测', '效率优化', '口岸'],
    rules: [
      '使用脱敏后的口岸通关数据，禁止还原任何个人或企业信息',
      '以通关时长预测误差为核心指标',
      '提交方案需附带效率瓶颈分析'
    ],
    evaluationCriteria: [
      '通关时长预测精度',
      '瓶颈识别与建议的合理性',
      '数据安全合规与方案完整性'
    ],
    scoringWeights: [
      { name: '预测精度', weight: 55 },
      { name: '瓶颈分析', weight: 25 },
      { name: '方案完整度', weight: 20 }
    ],
    rounds: [
      { name: '初赛', date: '2026-08-18 ~ 2026-09-18', desc: '线上评测实时排名' },
      { name: '复赛', date: '2026-09-19 ~ 2026-10-13', desc: '隐藏测试集与报告评审' }
    ],
    submissions: 0,
    leaderboard: []
  },
  {
    id: 'c8',
    title: '铁路集装箱班列调度赛',
    description: '优化中欧班列与国内集装箱班列的编组、到发时刻与装卸资源调度。',
    status: '已结束',
    category: '铁路',
    startDate: '2026-04-01',
    endDate: '2026-06-30',
    reward: '¥28,000',
    participants: 197,
    followed: false,
    registered: false,
    registrationStart: '2026-03-01',
    registrationEnd: '2026-03-31',
    submissionEnd: '2026-06-25',
    resultPublishDate: '2026-06-30',
    teamSizeMin: 1,
    teamSizeMax: 4,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 4,
    tags: ['班列调度', '编组优化', '铁路'],
    rules: [
      '使用官方提供的班列与装卸资源数据',
      '以班列正点率与装卸资源利用率综合评分',
      '提交求解方案与结果说明'
    ],
    evaluationCriteria: [
      '班列正点率与资源利用率',
      '调度方案的鲁棒性与可执行性',
      '算法效率与工程实现'
    ],
    scoringWeights: [
      { name: '调度性能', weight: 60 },
      { name: '鲁棒性', weight: 20 },
      { name: '工程实现', weight: 20 }
    ],
    rounds: [
      { name: '初赛', date: '2026-04-01 ~ 2026-05-20', desc: '标准场景线上评测' },
      { name: '复赛', date: '2026-05-21 ~ 2026-06-25', desc: '扰动场景与综合评审' }
    ],
    submissions: 143,
    leaderboard: [
      { rank: 1, team: '铁轨智行', score: 94.8 },
      { rank: 2, team: '班列优化组', score: 93.6 },
      { rank: 3, team: 'RailSched', score: 92.1 },
      { rank: 4, team: '钢铁驼队', score: 90.9 },
      { rank: 5, team: '枢纽调度', score: 89.4 }
    ]
  },
  {
    id: 'c9',
    title: '生鲜电商末端配送赛',
    description: '面向生鲜电商即时配送，优化骑手调度、冷链保温与时效履约综合指标。',
    status: '进行中',
    category: '其他',
    startDate: '2026-07-10',
    endDate: '2026-09-20',
    reward: '¥22,000',
    participants: 305,
    followed: false,
    registered: false,
    registrationStart: '2026-06-10',
    registrationEnd: '2026-07-09',
    submissionEnd: '2026-09-15',
    resultPublishDate: '2026-09-20',
    teamSizeMin: 1,
    teamSizeMax: 5,
    goldMedals: 1,
    silverMedals: 2,
    bronzeMedals: 6,
    tags: ['即时配送', '骑手调度', '生鲜'],
    rules: [
      '使用官方提供的订单与骑手轨迹数据',
      '以超时率与冷链达标率综合评分',
      '支持高峰时段实时调度场景'
    ],
    evaluationCriteria: [
      '超时率与履约时效',
      '冷链保温达标情况',
      '调度策略的实时性与扩展性'
    ],
    scoringWeights: [
      { name: '履约时效', weight: 50 },
      { name: '冷链达标', weight: 25 },
      { name: '实时扩展', weight: 25 }
    ],
    rounds: [
      { name: '初赛', date: '2026-07-10 ~ 2026-08-20', desc: '历史订单回放评测' },
      { name: '复赛', date: '2026-08-21 ~ 2026-09-15', desc: '实时仿真评测与答辩' }
    ],
    submissions: 88,
    leaderboard: [
      { rank: 1, team: '鲜达快送', score: 95.2 },
      { rank: 2, team: '冷链骑士', score: 94.1 },
      { rank: 3, team: '即时履约组', score: 92.8 },
      { rank: 4, team: 'FreshRoute', score: 91.3 },
      { rank: 5, team: '极速达', score: 90.0 }
    ]
  }
];

/**
 * 竞赛领域分类（对齐源网站 category-bar）
 */
const competitionCategories = ['全部领域', '港航', '口岸', '公路', '铁路', '航空', '特种运输', '多式联运', '物流枢纽', '其他'];

/**
 * 竞赛状态筛选（对齐源网站 filter-bar）
 */
const competitionStatusFilters = ['全部', '已关注', '已报名', '即将开始', '报名中', '进行中', '已结束'];

/**
 * 竞赛排序选项（对齐源网站 sort-box）
 */
const competitionSortOptions = ['默认排序', '截止时间', '奖金', '参与人数'];

/**
 * 任务数据（物流 AI 任务，对齐源网站 /api/tasks 详情结构）
 * @property {string} id - 唯一标识
 * @property {string} title - 任务标题
 * @property {string} description - 任务描述
 * @property {string} status - 状态：进行中 / 已完成 / 已关闭
 * @property {string} tag - 精选维度：热门 / 高收益 / 新上线 / 已验证（探索页用）
 * @property {string} domain - 所属领域
 * @property {string} reward - 奖励金额
 * @property {string} deadline - 截止日期
 * @property {number} participants - 参与人数
 * @property {string} taskType - 任务类型
 * @property {string} industry - 行业
 * @property {string} expectedFormat - 期望交付格式
 * @property {number} rewardAmount - 基础奖励金额（元）
 * @property {number} bonusReward - 加赏金额（元）
 * @property {Array} acceptanceCriteria - 验收标准
 * @property {number} executionCount - 执行次数
 * @property {number} submissionCount - 提交数
 * @property {number} sharesCount - 分享数
 * @property {number} viewsCount - 浏览数
 * @property {number} likesCount - 点赞数
 * @property {number} commentsCount - 评论数
 * @property {number} favoritesCount - 收藏数
 * @property {Array} bonusContributors - 加赏贡献者
 * @property {Array} relatedArtifactIds - 关联制品 id
 */
const tasks = [
  {
    id: 't1',
    title: '冷链运输温度异常预测模型',
    description: '构建一个可实时预测冷链运输过程中温度异常事件的深度学习模型，要求推理延迟低于 200ms。',
    status: '进行中',
    tag: '热门',
    domain: '冷链物流',
    reward: '¥2,000',
    deadline: '2026-09-15',
    participants: 87,
    taskType: '算法开发',
    industry: '冷链物流',
    expectedFormat: 'JSON + Python 脚本',
    rewardAmount: 2000,
    bonusReward: 500,
    acceptanceCriteria: [
      '模型在公开测试集上的温度异常检测 F1 分数不低于 0.92',
      '单条记录推理延迟不超过 200ms，支持批量推理',
      '提供完整的训练脚本、推理脚本与评估报告',
      '附异常阈值调优说明与可复现的依赖环境配置'
    ],
    executionCount: 214,
    submissionCount: 43,
    sharesCount: 18,
    viewsCount: 1520,
    likesCount: 96,
    commentsCount: 27,
    favoritesCount: 54,
    bonusContributors: [
      { name: '陈启航', initial: '陈', amount: '¥200' },
      { name: '林晓雅', initial: '林', amount: '¥300' }
    ],
    relatedArtifactIds: ['a2', 'a5']
  },
  {
    id: 't2',
    title: '末端配送路径动态优化算法',
    description: '针对高峰时段订单波动，设计动态路径优化算法，目标将平均配送时长降低 15%。',
    status: '进行中',
    tag: '高收益',
    domain: '城市配送',
    reward: '¥5,000',
    deadline: '2026-10-01',
    participants: 124,
    taskType: '算法开发',
    industry: '城市配送',
    expectedFormat: 'Python 脚本 + 报告',
    rewardAmount: 5000,
    bonusReward: 1500,
    acceptanceCriteria: [
      '在标准配送基准集上，平均配送时长相比基线降低不少于 15%',
      '支持 500 节点规模下的动态重规划，重规划耗时低于 3s',
      '提交可运行源码、配置说明与对比实验报告',
      '覆盖高峰期、恶劣天气等至少 3 类波动场景'
    ],
    executionCount: 389,
    submissionCount: 61,
    sharesCount: 32,
    viewsCount: 2610,
    likesCount: 158,
    commentsCount: 44,
    favoritesCount: 87,
    bonusContributors: [
      { name: '赵一鸣', initial: '赵', amount: '¥800' },
      { name: '王海峰', initial: '王', amount: '¥700' }
    ],
    relatedArtifactIds: ['a4']
  },
  {
    id: 't3',
    title: '港口集装箱图像标注众包',
    description: '对 5 万张港口集装箱图像进行箱号识别与箱况分类标注，构建高质量训练集。',
    status: '进行中',
    tag: '新上线',
    domain: '港口调度',
    reward: '¥800',
    deadline: '2026-08-31',
    participants: 302,
    taskType: '数据标注',
    industry: '港口物流',
    expectedFormat: 'JSON 标注文件',
    rewardAmount: 800,
    bonusReward: 200,
    acceptanceCriteria: [
      '箱号识别标注准确率不低于 99%，箱况分类标注一致性 Kappa 不低于 0.85',
      '标注结果符合 COCO 格式规范，含质量抽检记录',
      '按批次交付并附标注进度与质检报告',
      '修复抽检不合格样本并复检通过'
    ],
    executionCount: 1870,
    submissionCount: 305,
    sharesCount: 9,
    viewsCount: 8920,
    likesCount: 210,
    commentsCount: 68,
    favoritesCount: 133,
    bonusContributors: [
      { name: '周雨桐', initial: '周', amount: '¥100' }
    ],
    relatedArtifactIds: ['a3']
  },
  {
    id: 't4',
    title: '运输成本异常归因分析',
    description: '基于历史运单数据，定位运输成本异常波动的根因，并输出可解释的归因报告。',
    status: '已关闭',
    tag: '已验证',
    domain: '成本优化',
    reward: '¥3,000',
    deadline: '2026-07-31',
    participants: 56,
    taskType: '数据分析',
    industry: '运输成本',
    expectedFormat: 'Markdown 报告',
    rewardAmount: 3000,
    bonusReward: 800,
    acceptanceCriteria: [
      '覆盖至少 12 个月的运单数据，识别出不少于 5 类成本异常根因',
      '归因结论具备可解释性，并给出可量化的改善建议',
      '输出结构化报告与可复用的分析脚本',
      '通过领域专家评审与数据一致性校验'
    ],
    executionCount: 96,
    submissionCount: 28,
    sharesCount: 6,
    viewsCount: 1340,
    likesCount: 62,
    commentsCount: 15,
    favoritesCount: 31,
    bonusContributors: [],
    relatedArtifactIds: ['a8']
  },
  {
    id: 't5',
    title: '仓储库存周转预测',
    description: '融合销售、促销与季节因素，预测未来 30 天各 SKU 的库存周转需求。',
    status: '进行中',
    tag: '热门',
    domain: '智能仓储',
    reward: '¥4,000',
    deadline: '2026-09-20',
    participants: 98,
    taskType: '算法开发',
    industry: '智能仓储',
    expectedFormat: 'Python 脚本 + 报告',
    rewardAmount: 4000,
    bonusReward: 1000,
    acceptanceCriteria: [
      '未来 30 天 SKU 级周转预测 MAPE 不超过 12%',
      '支持促销、季节等多因子输入，并提供特征重要性解释',
      '提交可训练、可推理的完整工程代码',
      '附数据预处理说明与滚动验证结果'
    ],
    executionCount: 178,
    submissionCount: 39,
    sharesCount: 14,
    viewsCount: 2010,
    likesCount: 104,
    commentsCount: 23,
    favoritesCount: 49,
    bonusContributors: [
      { name: '郑博文', initial: '郑', amount: '¥400' }
    ],
    relatedArtifactIds: ['a6']
  },
  {
    id: 't6',
    title: '多式联运碳排放核算标准',
    description: '制定公路、铁路、水运多式联运场景下的碳排放核算方法，并输出工具实现。',
    status: '已完成',
    tag: '已验证',
    domain: '绿色物流',
    reward: '¥6,000',
    deadline: '2026-06-30',
    participants: 145,
    taskType: '标准制定',
    industry: '绿色物流',
    expectedFormat: '文档 + 工具',
    rewardAmount: 6000,
    bonusReward: 2000,
    acceptanceCriteria: [
      '核算方法符合温室气体协议与国内相关标准',
      '覆盖公路、铁路、水运三类模式的排放因子与边界',
      '输出可审计的碳积分核算工具与说明文档',
      '通过第三方机构评审与试点验证'
    ],
    executionCount: 132,
    submissionCount: 22,
    sharesCount: 41,
    viewsCount: 3280,
    likesCount: 187,
    commentsCount: 36,
    favoritesCount: 92,
    bonusContributors: [
      { name: '孙浩然', initial: '孙', amount: '¥1200' },
      { name: '钱思远', initial: '钱', amount: '¥800' }
    ],
    relatedArtifactIds: ['a7', 'a1']
  }
];

/**
 * arch.os 智能目录系统特性数据
 * @property {string} icon - 图标键名（对应 app.js 中 icons 集合）
 * @property {string} title - 特性标题
 * @property {string} desc - 特性描述
 */
const archOsFeatures = [
  { icon: 'cpu', title: 'AI 自动分类', desc: '制品上传即自动识别领域、行业与类型，无需手工打标，让目录保持实时有序。' },
  { icon: 'search', title: '自然语言检索', desc: '用一句话描述需求，人类开发者与 AI Agent 都能精准命中目标制品。' },
  { icon: 'folder', title: '版本化归档', desc: '自动记录制品演进历史，每一次更新与迭代均可追溯。' }
];

/**
 * 行业专区数据（物流细分领域）
 * @property {string} id - 唯一标识
 * @property {string} name - 行业名称
 * @property {number} count - 相关制品数量
 */
const industryZones = [
  { id: 'multimodal', name: '多式联运', count: 42 },
  { id: 'coldchain', name: '冷链物流', count: 38 },
  { id: 'seafreight', name: '海运物流', count: 31 },
  { id: 'port', name: '港口调度', count: 27 },
  { id: 'delivery', name: '城市配送', count: 45 },
  { id: 'supplychain', name: '供应链', count: 53 },
  { id: 'warehouse', name: '智能仓储', count: 49 },
  { id: 'express', name: '快递物流', count: 35 },
  { id: 'green', name: '绿色物流', count: 22 },
  { id: 'sorting', name: '分拣中心', count: 18 }
];

/**
 * 探索页（精选任务）筛选维度
 */
const featuredFilters = ['全部', '热门', '高收益', '新上线', '已验证'];

/**
 * 知识库文档数据（物流供应链知识资源）
 * @property {string} id - 唯一标识
 * @property {string} title - 文档标题
 * @property {string} category - 所属分类
 * @property {string} size - 文件大小
 * @property {string} date - 上传日期
 * @property {number} views - 浏览次数
 */
const knowledgeDocs = [
  { id: 'k1', title: '多式联运碳排放核算方法指南', category: '绿色物流', size: '2.4 MB', date: '2026-08-12', views: 328 },
  { id: 'k2', title: '冷链仓储温控标准与最佳实践', category: '冷链物流', size: '5.1 MB', date: '2026-08-05', views: 451 },
  { id: 'k3', title: '港口集装箱调度算法综述', category: '港口调度', size: '1.8 MB', date: '2026-07-28', views: 276 },
  { id: 'k4', title: '末端配送路径优化方法白皮书', category: '城市配送', size: '3.6 MB', date: '2026-07-19', views: 512 }
];

/**
 * Issues 议题数据（产品建议 / 技术问题 / 协作议题，对齐源网站 /api/issues 详情结构）
 * @property {string} id - 唯一标识
 * @property {string} title - 议题标题
 * @property {string} author - 提交人
 * @property {string} authorInitial - 头像首字母
 * @property {string} type - 类型：建议 / 问题 / 议题
 * @property {string} status - 状态：开放 / 已关闭
 * @property {string} time - 提交时间
 * @property {number} comments - 评论数
 * @property {string} description - 详细描述
 * @property {string} priority - 优先级：high / medium / low
 * @property {Array} labels - 标签
 * @property {Array} assignees - 指派人
 * @property {Array} issueComments - 评论列表
 * @property {boolean} isPinned - 是否置顶
 * @property {boolean} isAiCompleted - AI 是否已处理
 */
const issuesData = [
  {
    id: 'i1',
    title: '建议支持批量上传数据集并自动生成元数据',
    author: '陈启航',
    authorInitial: '陈',
    type: '建议',
    status: '开放',
    time: '3 天前',
    comments: 12,
    description: '目前上传数据集需要逐条填写领域、行业、格式等元数据，效率较低。希望支持批量上传多个文件，并由系统自动识别文件类型、推断领域与行业、生成标准元数据，用户只需确认或微调即可发布。',
    priority: 'medium',
    labels: [
      { name: '功能建议', color: 'blue' },
      { name: '数据管理', color: 'green' }
    ],
    assignees: [
      { name: '产品组', initial: '产' }
    ],
    issueComments: [
      { author: '王海峰', authorInitial: '王', time: '2 天前', text: '这个很实用，尤其是大规模数据集场景，建议同时支持自定义元数据模板。' },
      { author: 'AI 助手', authorInitial: 'AI', time: '1 天前', text: '已记录需求，评估后会在近期版本中规划批量导入能力。', isAI: true }
    ],
    isPinned: true,
    isAiCompleted: false
  },
  {
    id: 'i2',
    title: '多式联运路线可视化组件在移动端渲染异常',
    author: '林晓雅',
    authorInitial: '林',
    type: '问题',
    status: '开放',
    time: '1 周前',
    comments: 5,
    description: '在多式联运路线可视化大屏中，路线图在移动端（iOS Safari 与部分 Android 浏览器）出现节点错位与连线断裂，桌面端表现正常。已复现，疑似与缩放时的坐标换算有关。',
    priority: 'high',
    labels: [
      { name: 'bug', color: 'red' },
      { name: '可视化', color: 'orange' }
    ],
    assignees: [
      { name: '前端组', initial: '前' },
      { name: '郑博文', initial: '郑' }
    ],
    issueComments: [
      { author: '郑博文', authorInitial: '郑', time: '5 天前', text: '已定位到是 touch 事件坐标未做 devicePixelRatio 换算，正在修复。' },
      { author: '林晓雅', authorInitial: '林', time: '4 天前', text: '补充：横竖屏切换后问题会更明显，麻烦一并处理。' }
    ],
    isPinned: false,
    isAiCompleted: false
  },
  {
    id: 'i3',
    title: '发起「绿色物流碳积分核算标准」协作议题',
    author: '王海峰',
    authorInitial: '王',
    type: '议题',
    status: '开放',
    time: '2 周前',
    comments: 18,
    description: '提议联合社区成员共同制定绿色物流场景下的碳积分核算标准，覆盖公路、铁路、水运多式联运，统一排放因子口径与核算边界，为后续碳积分交易与绿色认证提供依据。',
    priority: 'medium',
    labels: [
      { name: '协作议题', color: 'purple' },
      { name: '绿色物流', color: 'green' },
      { name: '标准制定', color: 'blue' }
    ],
    assignees: [
      { name: '孙浩然', initial: '孙' },
      { name: '钱思远', initial: '钱' }
    ],
    issueComments: [
      { author: '孙浩然', authorInitial: '孙', time: '1 周前', text: '愿意牵头，我先整理一份排放因子的初稿供大家评审。' },
      { author: '钱思远', authorInitial: '钱', time: '6 天前', text: '可以对接此前多式联运碳排放核算任务（t6）的成果，避免重复工作。' },
      { author: '周雨桐', authorInitial: '周', time: '3 天前', text: '建议同步考虑核算工具的开源实现，方便社区落地。' }
    ],
    isPinned: true,
    isAiCompleted: false
  },
  {
    id: 'i4',
    title: '希望增加任务成果的自动化评测能力',
    author: '赵一鸣',
    authorInitial: '赵',
    type: '建议',
    status: '已关闭',
    time: '3 周前',
    comments: 9,
    description: '目前任务成果提交后依赖人工评审，周期较长。希望引入自动化评测，针对算法类任务可提供标准评测集与自动打分，缩短评审周期并提升公平性。',
    priority: 'medium',
    labels: [
      { name: '功能建议', color: 'blue' },
      { name: '评测', color: 'orange' }
    ],
    assignees: [],
    issueComments: [
      { author: '产品组', authorInitial: '产', time: '2 周前', text: '已纳入竞赛评测体系规划，任务侧自动化评测将复用同一套能力。' },
      { author: '赵一鸣', authorInitial: '赵', time: '1 周前', text: '好的，期待落地。' }
    ],
    isPinned: false,
    isAiCompleted: true
  }
];
