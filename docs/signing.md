# Windows 代码签名

## 当前状态

截至 2026-10-19，0.4.0 与 0.4.1 均未签名。本机未发现代码签名证书，GitHub 仓库也没有签名机密。
已接入基于 Windows 证书存储及 SignTool 的签名构建；取得真实证书前，不能生成显示已验证发布者的正式安装程序。
目前只验证了构建防护和验签失败路径，真实证书签名、安装提示及更新签名还需拿到证书后实测。

## 中国大陆个人申请

需要 CA 提供的个人代码签名证书，并完成本人身份和地址验证。
[Certum Standard Code Signing](https://support.certum.eu/en/code-signing-required-documents/) 支持个人申请，
个人申请另需本人名下的水、电、燃气或电话等账单。其云证书通过 SimplySign 提供密钥访问，
可用 Windows SignTool 签名。下单前应向 CA 核实当前是否受理中国大陆个人、中文材料要求、价格和云签名权限。
公开文档支持个人申请，不等于已经确认某一中国申请人的可签发资格。

[Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/quickstart) 的公共信任个人身份验证
目前限美国、加拿大，中国大陆个人不能采用这个方案。Private Trust 和测试证书不适合公开分发。
自签名证书也不能让其他用户的 Windows 自动信任发布者。

实名认证和支付由证书持有人在 CA 官方网站完成，不能把私钥、口令或身份证件提交到仓库或聊天中。
安装界面的发布者来自证书上的经验证身份；个人证书通常显示个人姓名，不能任意设为产品名称。

## 本地签名构建

先安装并登录 CA 的硬件令牌或云密钥提供程序，使代码签名证书及其私钥可通过 Windows
`CurrentUser\My` 证书存储访问。需要手机、PIN 或触摸验证时由证书持有人完成。
不是要求导出私钥为 PFX。

```powershell
# 查询的是公开证书指纹；不导出私钥。
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Select-Object Subject, Thumbprint, NotAfter

# 版本必须高于已经发布的 0.4.1。以下指纹请替换为实际值。
./build/package.ps1 -Version 0.4.2 -RequireSignature -SignCertificateThumbprint '<40位证书指纹>'
```

默认使用固定版本 Velopack 自带的 SignTool，也可以用 `-SignToolPath` 指定 Windows SDK 工具。
证书在 `LocalMachine\My` 时增加 `-SignMachineStore`。时间戳服务器默认是
`http://timestamp.digicert.com`，可用 `-TimestampServer` 指定 CA 支持的 RFC 3161 服务。

流程先为 ClipNest 主程序和三个自有程序集加 SHA256 签名及 RFC 3161 SHA256 时间戳，再生成便携 ZIP。
Velopack 在打包期间为生成的更新器和安装器签名。产物复制到发布目录前，校验安装器及实际更新包内的
主程序、程序集、`Squirrel.exe`：Windows Authenticode 必须有效，证书指纹必须匹配，必须带时间戳，
并通过 `signtool verify /pa /all`。任何失败都会终止，不生成可以继续发布的成功结果。

签名改变文件摘要，因此必须重新生成完整更新包、更新清单和 SHA256 校验文件，并用新版本号发布，
不能给已有的 0.4.0 文件补签后覆盖下载。

## GitHub Actions

普通构建工作流仍生成未签名的开发验证产物。`release.yml` 始终传入 `-RequireSignature`，
通过仓库变量 `CLIPNEST_SIGN_CERT_THUMBPRINT` 指定证书，缺失时立即失败。
仅配置指纹不够：签名提供程序和私钥访问权限也必须先配置在运行器上。
当前 GitHub 托管运行器尚未配置任何提供程序，因此此签名工作流暂不能执行。
0.4.1 采用本地构建、草稿上传与校验后公开的手动流程，暂以未签名包发布，并在发布说明中明确标注；工作流的签名要求保持不变。
确定 CA 后再接入其受支持的云签名或专用运行器；手机确认式云证书不应宣称已实现无人值守签名。

## 发布者与 SmartScreen

有效且受 Windows 信任的证书可以显示已验证的发布者。
SmartScreen 对新文件仍可能提示未建立信誉，EV 也不保证立即消除该提醒。
详情见 [微软 SmartScreen 说明](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation)。
签名证明来源与完整性，并不代替软件测试。

实现参考：[SignTool](https://learn.microsoft.com/windows/win32/seccrypto/signtool)、
[Velopack 签名流程](https://docs.velopack.io/packaging/signing)。
