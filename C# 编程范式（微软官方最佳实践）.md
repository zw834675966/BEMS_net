---
document: C# Coding Standards
type: reference
version: "1.1"
priority: 4
depends_on: []
language: zh-CN
---

# C# 编程范式（微软官方最佳实践）

> 适用范围：企业级 C# / .NET 项目（应用开发与类库开发）
> 依据来源：Microsoft Learn 与 dotnet 官方仓库指南

## 0. 版本基线（本项目约定）

1. 项目运行时基线：`.NET 10 LTS`。
2. 工具链基线：统一 SDK 与 CI 镜像版本，避免“本地可编译、流水线失败”。
3. 升级策略：按小版本窗口滚动升级，升级前执行依赖兼容清单与回归测试。

## 1. 核心范式

1. 代码可读性优先：优先选择清晰、可维护的表达方式，而非“技巧化”写法。
2. 现代 C# 优先：在团队可接受范围内使用现代语言特性，避免过时语法。
3. 异常用于异常路径：常见分支优先用条件判断或 `Try*` 模式，不用异常做流程控制。
4. 异步优先 I/O 场景：网络、数据库、文件读写默认采用 `async/await`。
5. API 一致性：命名、参数、返回值和错误语义在系统内保持统一。

## 2. 命名与代码风格

### 2.1 命名约定

1. 类型、命名空间、公共成员使用 `PascalCase`。
2. 方法参数、局部变量、私有字段使用 `camelCase`。
3. 私有字段使用前缀 `_`（如 `_orderRepository`）。
4. 接口名以 `I` 开头（如 `IOrderService`）。
5. 特性类以 `Attribute` 结尾（如 `AuditAttribute`）。
6. 非 Flags 枚举使用单数名；Flags 枚举使用复数名。

### 2.2 可读性优先规则

1. 优先使用 C# 关键字类型名：`int`、`string`，而非 `Int32`、`String`。
2. `var` 仅在右值可明显推断类型时使用。
3. 字符串拼接优先字符串插值 `$"..."`；大量拼接使用 `StringBuilder`。
4. 对可释放资源优先使用 `using` / `using var`。
5. 优先使用短路运算符 `&&`、`||`。

示例：

```csharp
using var conn = new SqlConnection(connectionString);

if (user is not null && user.IsActive)
{
    var message = $"User {user.Id} is active.";
    Console.WriteLine(message);
}
```

## 3. 异常处理范式

1. 只捕获你能处理并可恢复的异常。
2. 不要捕获过宽异常（如直接 `catch (Exception)`）后“吞掉”。
3. 从 `catch` 中重新抛出时使用 `throw;` 保留原始堆栈。
4. 对高频可预期失败，优先 `Try*`（如 `int.TryParse`、`TryGetValue`）。
5. 异步取消优先捕获 `OperationCanceledException`。

示例：

```csharp
try
{
    Process(order);
}
catch (DomainRuleException ex)
{
    _logger.LogWarning(ex, "Business rule violated.");
    return Result.Fail(ex.Message);
}
```

## 4. 异步与并发范式

### 4.1 I/O 与 CPU 场景区分

1. I/O 密集（HTTP/DB/文件）：直接 `await` 异步 API，不要滥用 `Task.Run`。
2. CPU 密集（重计算）：使用 `Task.Run` 转移到后台线程，保持调用线程响应性。

### 4.2 异步代码规范

1. 避免 `.Wait()`、`.Result` 阻塞等待，防止死锁与线程饥饿。
2. 可并行独立任务使用 `Task.WhenAll`。
3. 用 LINQ 生成任务时立即 `ToList()`/`ToArray()`，避免延迟执行导致并发失效。
4. 需要取消能力时传递 `CancellationToken`。

示例：

```csharp
public async Task<User[]> GetUsersAsync(IEnumerable<int> ids, CancellationToken ct)
{
    var tasks = ids.Select(id => _userService.GetUserAsync(id, ct)).ToArray();
    return await Task.WhenAll(tasks);
}
```

## 5. 类库与 API 设计范式（Framework Design Guidelines）

1. 面向使用者体验设计 API：易发现、易理解、行为可预测。
2. 公开 API 的命名、参数顺序、空值语义、异常语义保持一致。
3. 谨慎暴露可变状态，优先不可变对象或受控修改。
4. 设计可扩展点时保持最小必要面，避免过早抽象。
5. 若违反通用设计指南，必须有明确且可说明的收益。

## 6. 性能范式（在正确性之后）

1. 先测量后优化，避免凭感觉优化。
2. 热路径减少分配与复制，必要时使用 `Span<T>` / `Memory<T>` 等机制。
3. 避免在常规流程中依赖异常分支（异常有成本）。
4. 高频文本拼接、序列化、集合操作应进行基准验证。

## 7. 团队落地建议

1. 使用 `.editorconfig` 固化命名和格式规则。
2. 启用 Roslyn 分析器与 CA 规则，在 CI 中强制执行。
3. PR 检查清单至少覆盖：命名一致性、异常处理、异步正确性、取消传递、可测试性。
4. 对关键范式提供模板代码（如服务层、仓储层、后台任务）降低偏差。

## 8. 快速自检清单

1. 是否使用统一命名规则（`PascalCase`/`camelCase`/`_field`）？
2. 是否避免了 `.Result/.Wait()`？
3. 是否只捕获可处理异常，并在必要时 `throw;`？
4. I/O 场景是否全部异步化？
5. 是否对并发任务使用了 `Task.WhenAll`？
6. 是否为可取消操作传入 `CancellationToken`？
7. 是否使用分析器和 CI 进行自动约束？

## 9. 从 0 开始制作 C# 项目（任务清单，替代周执行表）

> 说明：以下清单按微软官方推荐流程重排，可直接作为项目启动执行单。

### 9.1 环境与基线

- [ ] 安装 .NET SDK（建议 LTS，当前可选 `.NET 10`），不要只装 Runtime。
- [ ] 执行 `dotnet --info` 与 `dotnet --list-sdks`，确认 SDK 可用且 PATH 正常。
- [ ] 统一团队 SDK 版本（提交 `global.json`），避免本地与 CI 版本漂移。

### 9.2 建立解决方案骨架

- [ ] 创建仓库根目录并初始化解决方案：`dotnet new sln -n <SolutionName>`。
- [ ] 按场景创建项目模板：
  - [ ] 控制台：`dotnet new console -o src/<AppName>`
  - [ ] 类库：`dotnet new classlib -o src/<LibName>`
  - [ ] Web API：`dotnet new webapi -o src/<ApiName>`
  - [ ] 单元测试：`dotnet new xunit -o tests/<AppName>.Tests`
- [ ] 将项目加入解决方案：`dotnet sln add <path-to-csproj>`。

### 9.3 项目依赖与分层

- [ ] 配置项目引用（推荐 .NET 10 语法）：`dotnet reference add <ProjectReferencePath> --project <ProjectPath>`。
- [ ] 若团队仍在 .NET 9 或更早，使用兼容写法：`dotnet add <ProjectPath> reference <ProjectReferencePath>`。
- [ ] 确认 `src/` 与 `tests/` 分离，避免测试与业务代码混放。

### 9.4 代码规范与质量门禁

- [ ] 建立 `.editorconfig`，固化命名、格式、可空性、分析器策略。
- [ ] 在 CI 中启用构建 + 测试 + 风格检查（至少 `dotnet build` + `dotnet test`）。
- [ ] 增加格式化检查：`dotnet format --verify-no-changes`。

### 9.5 开发与验证循环

- [ ] 日常开发循环：`dotnet build` -> `dotnet test` -> `dotnet run`。
- [ ] 每次新增功能至少补 1 个单元测试（xUnit）。
- [ ] 关键路径采用“先写失败测试，再最小实现，再重构”的 TDD 节奏。

### 9.6 发布与交付

- [ ] 发布前使用 Release 构建：`dotnet publish -c Release`。
- [ ] 明确发布产物目录与运行方式（Framework-dependent / Self-contained）。
- [ ] 在发布分支前执行完整回归清单（构建、测试、格式、静态分析）。

### 9.7 完成定义（DoD）

- [ ] 解决方案可一键构建（本地与 CI 一致）。
- [ ] 核心功能有自动化测试覆盖。
- [ ] 代码规范和分析器无阻断告警。
- [ ] 文档与启动命令可被新成员按步骤复现。

## 10. 官方参考链接

1. .NET Coding Conventions（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
2. Identifier Names（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
3. Best Practices for Exceptions（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions
4. Asynchronous Programming with async/await（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/
5. Asynchronous Programming Scenarios（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios
6. Framework Design Guidelines（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
7. dotnet/runtime C# Coding Style（GitHub）  
   https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md
8. Install .NET on Windows（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/core/install/windows
9. Create .NET projects and solutions with the CLI（Microsoft Learn）  
   https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-new-sdk-templates
10. Manage solutions with `dotnet sln`（Microsoft Learn）  
    https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-sln
11. Add project references with `dotnet reference add`（Microsoft Learn）  
    https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-reference-add
12. Build a C# app with .NET CLI（Microsoft Learn）  
    https://learn.microsoft.com/en-us/dotnet/core/tutorials/with-visual-studio-code
13. Test a .NET class library with xUnit（Microsoft Learn）  
    https://learn.microsoft.com/en-us/dotnet/core/tutorials/testing-library
14. What's new in .NET 10 CLI（Microsoft Learn）  
    https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/sdk#net-cli
