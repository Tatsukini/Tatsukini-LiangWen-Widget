# 梁文挂件 (LiangWen Widget)

一个很小的 Windows 桌面挂件：用 DeepSeek 创始人名字的最后一个字（梁文**峰**）来显示
DeepSeek API 的实时状态，并叠加峰谷价倒计时与 31 档头像轮播。

![peak](https://github.com/sukholovskyyy/dsh-liang-price-badge/raw/main/assets/peak.jpg)

## 结构

| 区域 | 内容 |
| --- | --- |
| 顶部 | 峰/谷 倒计时（到下一次峰谷切换）+ QoderCN 错峰折扣提示行 |
| 中部 | 头像（按当前时段进度取帧）+ 三个大字 梁 / 文 / 状态字 |
| 描述 | 当前 API 延迟、余额、峰谷时段 |
| 左下 | `API 充值` 超链接（platform.deepseek.com） |
| 右下 | 按余额折算的 未命中 / 命中 / 输出 token 额度（点击切换 flash/pro） |

状态字（优先级从高到低）：**崩**（接口瘫痪）> **活**（恢复后 5 秒内）> **满**（拥挤/限流）
> **停**（余额 < 100 万 token）> **峰 / 谷**（峰谷价时段）。颜色分别为 浅粉 / 淡蓝 / 黄 /
灰白 / 深灰 / 浅绿。

背景默认纯黑；把图片拖进窗口即设为背景（UniformToFill 铺满），并按图片平均亮度自动叠加
50%→0% 的黑色压暗层以保证文字可读。

## 状态检测

定时轮询 `GET /user/balance`（不消耗对话、不触发主动拒绝）：高延迟或 429 记为拥挤，
5xx / 超时 / 连接失败记为瘫痪，瘫痪后首次成功记为复活。余额解析兼容根节点
`total_balance` 与 `balance_infos[]` 两种返回结构。

## 峰谷时段

北京时间周一至周五（不含法定节假日）09:00–12:00、14:00–18:00 为高峰，其余为空闲。
节假日表内置 2026 年安排（国办发明电〔2025〕7号）与 2027-01-01，需逐年补表。

## QoderCN 错峰折扣提示

顶部倒计时下方一行随时间切换（与上面的 DeepSeek 峰谷窗口相互独立）：每日 22:00–次日 08:00
（北京时间，含周末与节假日）为 Qoder 错峰折扣时段，显示淡灰绿「QoderCN低峰折扣」；08:00–22:00
常规时段显示淡红「QoderCN 高峰」。时段与力度见 [Qoder 错峰折扣文档](https://docs.qoder.cn/product-overview/qwen-3-7-series-model-staggering-discount)。

## 构建

需要 .NET 8 SDK 与 Microsoft.WindowsDesktop.App 8.0.x 运行时。

```powershell
dotnet publish -c Release -o dist
```

产物为框架依赖单文件 `dist/LiangWenWidget.exe`（约 190 KB，依赖已安装桌面运行时）。

## 配置

首次运行弹窗输入 API Key（与 Base URL），保存到 exe 同级的 `config.json`。可在
`config.json` 里调整：轮询间隔、超时、拥挤延迟阈值、低余额阈值、复活显示时长、模型
（flash/pro）、各模型定价（元/百万 token）、是否显示头像等。`config.json` 含密钥，
已在 `.gitignore` 中忽略，请勿提交。

## 许可与致谢

- 代码：MIT（见 `LICENSE`）。
- 31 张档位图 + 峰谷日历逻辑：来自第三方 MIT 项目，详见 `THIRD-PARTY-NOTICES.md`。

## 免责声明

纯属网络恶搞与视觉玩梗，不代表梁文锋、DeepSeek 或任何机构的真实观点。
