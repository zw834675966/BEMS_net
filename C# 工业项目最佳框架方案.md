---
document: Architecture Reference
type: reference
version: "1.1"
priority: 4
depends_on: [ADD]
language: zh-CN
---

# **C# 建筑能源管理系统 (BEMS) 权威架构指南：面向 2026 年的最佳实践 (对齐版)**

> 本文为技术参考文档，约束优先级低于 `Charter.md` 与 `ADD.md`。

## **1. 执行摘要与架构哲学**

在“双碳”目标与数字化转型的驱动下，建筑能源管理系统 (BEMS) 正从简单的能耗记录工具演变为集“监测、控制、优化、计费”于一体的智能决策平台。2026 年，随着 **.NET 10** 的发布，C# 再次确立了其在高性能边缘计算与 Windows 11 环境下的统治地位。

本报告基于最新的《用户需求规格说明书 (SRS)》，为 BEMS 项目构建 Windows 11 单机/集群架构方案。我们核心的架构哲学是：**“Blazor 优先 (Blazor-First)”** 与 **“数据驱动 (Data-Driven)”**。

**首版可交付口径（6 个月）：** 聚焦单站点、单控制链路（Modbus TCP）、2D 可视化与关键链路可追踪，优先确保可部署、可运维、可验收。  
**二期目标口径：** 在首版稳定运行基础上，扩展 BACnet/IR 接入、3D 楼宇可视化与全量可观测性能力，逐步提升覆盖面与自动化深度。

### **核心架构支柱：**
1.  **交互层：** 纯 Blazor Web App，首版交付 2D 能源仪表盘，二期扩展 3D 楼宇可视化。
2.  **通讯层：** 首版聚焦 Modbus TCP，二期扩展 BACnet/IR，结合 Polly 弹性策略。
3.  **数据层：** **PostgreSQL + TimescaleDB** 单一主方案，统一处理关系型业务与时序能耗数据。
4.  **观测层：** 首版采用最小可观测性（日志 + traceId + 关键链路 tracing），二期再扩展全量 OpenTelemetry。

---

## **2. 交互层：沉浸式能源座舱 (Energy Cockpit)**

BEMS 的用户界面不再是枯燥的表格，而是需要直观展示“能流图”、“楼宇热力图”的数字化座舱。

### **2.1 最佳选型：纯 Blazor Web App (.NET 10)**

*   **Blazor Web App 的角色：** 作为统一的“应用外壳 + 业务内容”。
    *   **统一交付：** 采用 Blazor Web App（可按场景选择 Interactive Server / Interactive WebAssembly / Auto）统一构建控制室与远程访问端。
    *   **可视化能力：** 集成 ECharts/Three.js，实现桑基图 (Sankey Diagram)、热力图与 3D 楼宇模型。
    *   **多端访问：** 控制室大屏使用浏览器全屏或 PWA kiosk 模式；物业管理员直接通过浏览器访问同一套前端。
    *   **工程收益：** 保持纯 Web 技术路线，降低维护复杂度，聚焦单一 UI 技术栈。

---

## **3. 神经系统层：楼宇通讯与边缘控制**

不同于工厂自动化的 PLC 垄断，BEMS 面临的是极其碎片化的设备环境（电表、水表、中央空调、分体空调）。

### **3.1 协议栈选型：首版 Modbus TCP，二期 BACnet**

*   **Modbus TCP (FluentModbus):**
    *   用于采集智能电表、水表及通用 I/O 模块。
    *   **性能优化：** 使用 .NET 10 的 `Span<byte>` 进行零拷贝解析，在单线程内即可处理 1000+ 电表的秒级轮询。
    *   **配置规范：** 点位地址统一使用 `0-based` 偏移地址；`unitId` 必填；`32-bit/Float` 点位必须明确 `wordOrder`。
*   **BACnet/IP (BACnetClient)（二期）:**
    *   用于对接楼宇自控系统 (BAS) 及大型冷水机组 (Chiller)。
    *   **策略：** 优先采用“订阅 (COV)”模式而非轮询，降低网络负载。

### **3.2 暖通空调 (HVAC) 专项控制策略（首版从简，二期扩展）**

针对 SRS 中提出的复杂空调控制需求，架构层面需提供抽象接口 `IClimateControlService`，并实现多种适配器：

*   **多联机 (VRF) 适配器（二期）：**
    *   通过 **Modbus/BACnet 硬件网关** (如 CoolAutomation) 对接。
    *   实现“温限锁定”逻辑：在软件层拦截低于 26℃ 的设定指令，强制修正后再下发。
*   **分体空调 (Split AC) 适配器（二期）：**
    *   **红外与功率融合双控：**
        *   **下行 (控制)：** 通过 IR Blaster 发射红外码（模拟遥控器）。
        *   **上行 (反馈)：** 通过智能插座读取实时功率。如果发送了“开机”指令但功率保持为 0，系统应判定为控制失败并触发重试或报警。

### **3.3 通信韧性 (Resilience)**

*   **Polly 策略库：**
    *   对网关请求封装 `Retry` + `CircuitBreaker` + `Fallback`。
    *   默认重试上限建议 `<= 2`（与当前闭环验收口径一致）；仅在非控制类读取链路可按风险评估提高重试次数。
    *   针对红外发射器（UDP 协议），必须实现应用层 ACK 与功率反馈闭环确认。

---

## **4. 数据层：PostgreSQL 为底座，TimescaleDB 为增强**

在 BEMS 场景下，**PostgreSQL** 是稳定底座，**TimescaleDB** 是高性价比增强选项。

### **4.1 选型理由**
1.  **数据关联性强：** 能耗数据（时序）必须与房间、租户、费率（关系型）进行强关联查询（JOIN）。InfluxDB 难以处理这种复杂的 SQL 分析。
2.  **运维简化：** TimescaleDB 作为 PG 扩展可复用同一工具链；受限环境下也可回退到“PG 分区 + 物化视图”。
3.  **强大的分析能力：** 利用 SQL 窗口函数 (`LAG`, `LEAD`, `SUM OVER`) 轻松计算“同比”、“环比”及“尖峰平谷”分时电费。

### **4.2 存储策略**
*   **热数据 (Hot)：** 最近 3 个月的秒级原始遥测数据，存储于 SSD。
*   **温数据 (Warm)：** 聚合后的 15 分钟/1 小时粒度能耗数据，保留 5 年。
*   **冷数据 (Cold)：** 过期分区自动压缩并归档至对象存储 (S3/MinIO)。

### **4.3 首版执行约束（单一数据库路线）**
1. 首版固定采用 Timescale 扩展，不引入 PG Native 双模式 DDL。
2. 统一使用版本化迁移脚本管理 `telemetry` 与聚合视图对象。
3. 若二期需要回退到 PG Native，需单独立项并完成并行校验、性能对比和回滚预案验证。

---

## **5. 可观测性 (Observability) 与 系统健壮性**

### **5.1 链路追踪 (Distributed Tracing)**
首版以最小可观测性落地：关键操作链路生成 Trace ID，并在日志中贯通请求上下文；二期再扩展为全量 OpenTelemetry。
*   **场景示例：** 用户在 HMI 点击“开灯” -> API 收到请求 -> Modbus 驱动发出指令 -> 网关响应超时。
*   **价值：** 运维人员可在 Grafana Tempo 中看到完整的调用链路，瞬间定位是网络问题还是设备故障。

### **5.2 Native AOT (.NET 10)**
对于负责数据采集和协议转换的**核心网关服务 (Gateway Service)**，强制开启 **Native AOT** 编译。
*   **启动速度：** < 500ms，确保断电重启后能最快恢复数据采集。
*   **内存占用：** 减少 50% 以上，允许在 Windows 11 后台静默运行。
*   **兼容性保障：** 引入 **AOT Compatibility Scanner**，严格审查第三方 NuGet 包（尤其是 ORM 与 IoC 容器）的 AOT 支持情况。

---

## **6. 综合架构图谱 (2026 BEMS 版)**

| 架构层级 | 推荐技术/库 | 关键选型理由 |
| :--- | :--- | :--- |
| **运行时** | **.NET 10 (LTS)** | 最新的长期支持版本，SIMD 与 AOT 性能巅峰。 |
| **HMI 框架** | **纯 Blazor Web App** | 单一 UI 技术栈，统一大屏与远程访问，降低独立开发者维护成本。 |
| **应用架构** | **Clean Architecture + MediatR + FluentValidation** | 与 Blazor Web App 一致的分层与解耦模式，降低长期维护成本。 |
| **总线协议** | **Modbus TCP（首版）/ BACnet（二期）** | 先完成单链路可交付，再扩展楼宇协议覆盖。 |
| **空调控制** | **首版仅电表/通用点位；VRF/IR 二期** | 降低首版集成复杂度，控制硬件联调风险。 |
| **即时通讯** | **MQTTnet v5** | 能够处理弱网环境下的数据上报 (Sparkplug B)。 |
| **数据库** | **PostgreSQL + TimescaleDB** | 关系数据与时序能耗数据的完美融合。 |
| **ORM** | **EF Core 10 + Dapper** | 复杂业务用 EF，海量写入用 Dapper。 |
| **可观测性** | **结构化日志 + traceId（首版）/ OpenTelemetry（二期）** | 首版先保障可排障，二期再补全链路观测。 |
| **HMI 备选** | **PWA / Electron（按需）** | 当需要离线能力或桌面壳时，以 Web 技术延伸而非引入第二套原生 UI 栈。 |
| **CQRS 层** | **轻 CQRS：MediatR + FluentValidation** | 保留命令/查询分层，避免首版过度工程化。 |
| **Modbus 库** | **FluentModbus** | 轻量级，`Span<byte>` 零拷贝，async/await。 |
| **韧性** | **Polly** | 重试/熔断/降级策略 (红外/无线设备)。 |
| **开发编排** | **.NET Aspire (可选)** | 微服务本地开发编排与自动化部署。 |

---

## **7. 竞品技术方案对比**

| 维度 | 本方案 (.NET 10) | 传统组态 + MySQL | Node.js + InfluxDB |
| :--- | :--- | :--- | :--- |
| **启动性能** | < 500ms (AOT) | 10s+ (Java 冷启动) | 2-3s |
| **时序+关系 JOIN** | ✅ TimescaleDB 原生SQL | ❌ 需双库异构 | ❌ InfluxQL 不支持 |
| **HMI 体验** | 纯 Blazor 原生级 | 组态软件 (功能受限) | Web UI (性能受限) |
| **AOT 编译** | ✅ .NET 10 原生支持 | ❌ | ❌ |
| **链路观测** | ✅ 首版关键链路 traceId（二期扩展 OpenTelemetry） | ❌ 需额外集成 | 部分支持 |
| **类型安全** | ✅ 强类型 C# | 弱 (脚本语言) | 弱 (JavaScript) |

---

## **8. 独立开发者 6 个月落地路线图（任务清单制）**

### **8.1 范围锁定（首版）**
* 仅实现 **Modbus TCP** 控制链路（暂不实现 BACnet/IR）。
* UI 仅实现 **2D 仪表盘**（Blazor 图表），3D 可视化放入二期。
* 数据库固定为 **PostgreSQL + TimescaleDB**，不做双模式切换。
* 采用 **轻 CQRS**：保留 MediatR 命令/查询分层，不引入复杂 Pipeline。
* **AOT 仅用于采集服务**；业务 API 与 HMI 常规发布。
* 可观测性采用最小集：**结构化日志 + traceId + 关键链路 tracing**。

### **8.2 从 0 开始的任务清单（单人优先级）**
> 说明：以下任务按依赖顺序排列；每项均以“可验收”作为完成标准，可映射到 24 周节奏执行。

| ID | 任务 | 参考命令/方法 | 完成标准（DoD） |
| :--- | :--- | :--- | :--- |
| **T01** | 安装与校验开发环境（.NET SDK、CLI） | `dotnet --info` / `dotnet --version` | 本机可稳定执行 .NET CLI，版本与目标框架一致 |
| **T02** | 初始化解决方案与项目骨架（Blazor + API + Gateway + Tests） | `dotnet new sln`、`dotnet new blazor`、`dotnet sln add`（明确采用 `.sln` 或 `.slnx`） | 解决方案可一键还原与构建，目录结构固定，团队统一解文件格式 |
| **T03** | 锁定 SDK 与模板策略（Blazor） | `dotnet new globaljson --sdk-version <version> --roll-forward latestFeature`、`dotnet new blazor -h`（确认 `--interactivity`） | SDK 版本可复现，首版渲染模式在文档与代码一致（建议 Server/Auto 二选一） |
| **T04** | 建立代码规范与基础质量门禁 | `.editorconfig`、`dotnet format --verify-no-changes` | 本地与 CI 均可执行格式检查，输出稳定 |
| **T05** | 搭建数据库与 Timescale 扩展 | SQL 初始化脚本 + 环境变量配置 | 可创建库、可连通、可写入/查询基础表 |
| **T06** | 定义核心数据模型（设备/点位/遥测/告警） | EF Core 实体与配置 | 模型评审通过，字段与业务词汇一致 |
| **T07** | 建立迁移流程（开发/测试/生产） | `dotnet ef migrations add`、`dotnet ef database update` | 本地迁移可重复执行；迁移文件纳入版本管理 |
| **T08** | 建立生产迁移发布方式（脚本优先） | `dotnet ef migrations script --idempotent` | 能生成可审阅 SQL 脚本并通过预发验证 |
| **T09** | 完成 Modbus TCP 驱动封装 | 连接池、寄存器读写、点位映射 | 对仿真设备稳定读写，异常码可追踪 |
| **T10** | 完成采集调度与入库链路 | 定时轮询 + 批量写入 | 秒级采集跑通，数据延迟与丢点率可量化 |
| **T11** | 实施采集韧性（重试/超时/断线重连） | Polly 策略（限首版最小集） | 常见网络抖动下可自动恢复且不雪崩 |
| **T12** | 采集服务 AOT 发布 | `dotnet publish`（AOT 配置） | 生成可部署包，启动时间与内存基线达标 |
| **T13** | 实施轻 CQRS（命令/查询分离） | MediatR + FluentValidation | 关键业务路径已分离，未引入复杂 Pipeline |
| **T14** | 交付首版业务 API（设备/点位/实时值） | OpenAPI + 集成测试 | 关键接口可用、错误码一致、鉴权策略明确 |
| **T15** | 交付遥测聚合与告警基础能力 | SQL 聚合视图 + 阈值规则 | 日/周/月统计正确，告警可确认与恢复 |
| **T16** | 交付 2D Blazor 仪表盘 | 图表组件 + 状态卡 + 告警面板 | 首页可展示实时/历史关键指标，无阻塞卡顿 |
| **T17** | 交付控制闭环（UI -> API -> Gateway） | 控制命令链路 + 回执 | 控制成功/失败可见，具备操作审计 |
| **T18** | 落地最小可观测性 | 结构化日志 + traceId + 关键链路 tracing | 可基于一次故障在 10 分钟内定位到模块级 |
| **T19** | 安全与审计最小集 | 本地账号、RBAC、审计日志 | 权限越权用例被拦截，关键操作可追溯 |
| **T20** | 自动化测试与回归基线 | `dotnet test` | 核心单测/集成测试通过，失败可复现 |
| **T21** | 稳定性与容量验证 | Soak Test + 压测报告 | 连续运行达标，无明显内存/连接泄漏 |
| **T22** | 部署、验收与二期规划 | 发布包、运维手册、缺陷清单 | 首版上线可运维，二期范围（3D/BACnet/IR）冻结 |

### **8.3 任务阶段闸门（替代按周闸门）**
1. **G1（T01-T04）工程闸门：** 能从空目录创建、构建、格式检查完整通过。  
2. **G2（T05-T08）数据闸门：** 迁移流程在本地与预发可重复，生产 SQL 脚本可审阅。  
3. **G3（T09-T12）采集闸门：** Modbus 采集链路稳定，AOT 包可部署。  
4. **G4（T13-T17）业务闸门：** API + 2D HMI + 控制闭环打通。  
5. **G5（T18-T21）质量闸门：** 可观测性、测试、稳定性满足验收阈值。  
6. **G6（T22）交付闸门：** 首版上线可运维，二期 Backlog 已冻结。  

---

### **9. 结语**

本架构方案完全摒弃了传统的"组态软件"思维，拥抱现代化的 IT 技术栈。首版通过 **.NET 10 AOT（采集服务）** 保证底层采集性能，通过 **TimescaleDB** 挖掘能耗数据价值，通过 **纯 Blazor Web App** 提供统一的大屏与远程用户体验，并以 **轻 CQRS + 最小可观测性** 控制复杂度；二期再扩展 3D 可视化、BACnet/IR 设备接入与全量 OpenTelemetry 能力。

---

## **10. 独立开发者优先的框架方案（稳定 + 快速上线）**

> 目标：在“单人可维护”的前提下，优先实现可上线、可回滚、可排障，再逐步扩展高级能力。

### **10.1 目标架构（MVP）**

1. **架构形态：模块化单体（Modular Monolith）优先**
   * 单仓库、单发布单元、清晰边界（Domain/Application/Infrastructure/Web）。
   * 避免首版拆微服务，降低部署和运维复杂度。
2. **运行形态：一个 Web 进程 + 一个采集 Worker**
   * `Bems.Web`：Blazor + API + 鉴权 + 管理后台。
   * `Bems.Gateway.Worker`：设备采集、重试、批量入库（可 AOT）。
3. **数据形态：一个 PostgreSQL（含 Timescale 扩展）**
   * 统一业务数据与时序数据，减少跨库一致性问题。
4. **AI 形态：可插拔能力，不侵入主交易链路**
   * AI 仅用于“问答/分析/解释/辅助报表”，不直接驱动控制指令。

### **10.2 首版技术选型（独立开发者最小可行）**

| 层 | 首版选择 | 原因 |
| :--- | :--- | :--- |
| UI + API | Blazor Web App (.NET 10) | 单技术栈、开发效率高、维护成本低 |
| 业务分层 | Clean Architecture（轻量化） | 约束边界，防止代码腐化 |
| 数据访问 | EF Core + Dapper（热路径） | 开发效率与性能平衡 |
| 数据库 | PostgreSQL + TimescaleDB | 关系 + 时序统一 |
| 采集协议 | Modbus TCP | BEMS 首版落地最快 |
| 弹性 | Polly | 重试、熔断、超时标准化 |
| 部署 | Azure App Service + GitHub Actions | PaaS 快速上线，运维负担低 |
| 配置/密钥 | App Settings + Key Vault（可选） | 降低泄漏风险，方便切换环境 |
| 可观测性 | 结构化日志 + TraceId + Health Checks | 故障定位速度优先 |

---

## **11. 稳定快速上线的部署蓝图（单人可执行）**

### **11.1 环境策略**

1. **仅保留两套环境**：`staging` 与 `production`。
2. **分支策略最小化**：`main`（生产）、`develop`（预发）。
3. **配置分离**：所有连接串/API Key 仅放环境变量或机密管理，不入库。

### **11.2 CI/CD 最小闭环**

1. CI（每次 PR）
   * `dotnet restore`
   * `dotnet build -c Release`
   * `dotnet test -c Release`
   * `dotnet format --verify-no-changes`
2. CD（合并到主干）
   * `dotnet publish -c Release`
   * 自动部署到 `staging`
   * 冒烟检查通过后推进到 `production`

### **11.3 发布模式建议（按场景）**

1. **Web/API 默认：Framework-dependent**
   * 体积小、部署快、与 App Service 运行时协同好。
2. **采集 Worker：Self-contained / AOT（按兼容性验证后启用）**
   * 适合启动速度与内存敏感场景。
3. **若指定 RID，必须显式声明 `--self-contained true|false`**
   * 避免发布行为歧义（.NET SDK 兼容性变更点）。

### **11.4 生产稳定性清单（上线前必须通过）**

- [ ] `/health` 可用，数据库与关键依赖可探活。
- [ ] 关键链路日志含 `traceId`、设备ID、命令ID。
- [ ] 配置了请求超时、重试上限、并发限制。
- [ ] 发布脚本可重复执行，支持“一键回滚到上一个版本”。
- [ ] 生产异常可在 10 分钟内定位到模块级（Web/API/Worker/DB）。

---

## **12. Azure OpenAI 集成方案（可选增强，非阻断主链路）**

> 本节采用 `Azure.AI.OpenAI` 官方 SDK 方案；适用于“智能报表解释、能耗问答、告警摘要”。

### **12.1 集成定位（建议）**

1. **做什么**
   * 周/月能耗异常摘要
   * 告警聚合解释（按楼层/设备/时间窗）
   * 运维知识问答（基于知识库/RAG）
2. **不做什么**
   * 不让 AI 直接下发控制指令
   * 不让 AI 绕过业务规则/权限系统

### **12.2 依赖与认证**

```bash
dotnet add package Azure.AI.OpenAI
dotnet add package Azure.Identity
```

```bash
AZURE_OPENAI_ENDPOINT=https://<resource>.openai.azure.com
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4o-mini
```

生产建议：
1. 首选 Microsoft Entra ID（托管身份/服务主体）。
2. 若使用 `DefaultAzureCredential`，生产环境应配置确定性凭据策略，避免凭据链漂移。
3. 客户端复用单例，避免频繁创建导致吞吐抖动。

### **12.3 应用集成落点（BEMS）**

1. `Bems.Application.AI`：定义 `IInsightSummaryService`。
2. `Bems.Infrastructure.AI.AzureOpenAI`：封装 `AzureOpenAIClient` 与 `ChatClient`。
3. `Bems.Web`：仅暴露“摘要/问答”接口，不暴露原始模型调用细节。
4. 所有 AI 输出均走业务校验层（权限、字段白名单、脱敏策略）。

### **12.4 稳定性与成本控制**

1. 对 429/5xx 增加指数退避重试（有上限）。
2. 长响应采用流式输出，避免前端长时间无反馈。
3. 记录 token 用量、调用延迟、失败率，作为运维指标。
4. 限制最大输出 token 与并发会话数，防止成本失控。

---

## **13. 官方资料索引（本次优化依据）**

1. .NET 发布总览  
   https://learn.microsoft.com/en-us/dotnet/core/deploying/
2. Single-file 发布  
   https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
3. Trimming 选项  
   https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trimming-options
4. Azure App Service 配置 ASP.NET Core  
   https://learn.microsoft.com/en-us/azure/app-service/configure-language-dotnetcore
5. Azure App Service 快速部署 ASP.NET  
   https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore
6. Azure SDK for .NET（ASP.NET Core 指南）  
   https://learn.microsoft.com/en-us/dotnet/azure/sdk/aspnetcore-guidance
7. Azure Identity 认证最佳实践  
   https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/best-practices
8. Azure OpenAI .NET 快速开始（AI App 模板）  
   https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/ai-templates
9. Azure OpenAI .NET 聊天应用快速开始  
   https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-chat-app
10. GitHub Actions 与 .NET 概览  
    https://learn.microsoft.com/en-us/dotnet/devops/github-actions-overview
