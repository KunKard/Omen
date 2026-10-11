# Tools

## verify-compile.cmd

**用途**：在不启动 Unity 编辑器的前提下，复现 Unity 的脚本编译，用来在改动代码后快速确认「能不能编译过」。

**为什么需要它**：开发期间 Unity 编辑器通常是开着的。此时跑 `Unity.exe -batchmode` 会和编辑器抢项目锁，
要么失败要么干扰编辑器。这个脚本绕开编辑器，直接调用 Roslyn。

**原理**：Unity 的编译环境 = Mono 的 `4.7.1-api` 框架程序集 + `Data\Managed\UnityEngine\` 下的
87 个模块程序集。脚本把这些喂给 Roslyn，得到与 Unity Console 一致的诊断。

**用法**：

```
Tools\verify-compile.cmd
```

退出码 `0` = 编译通过；非 `0` = 编译失败，输出里会列出错误文件与行号。

**注意**：

- 里面的 Unity 安装路径是硬编码的（`D:\Program Files\2022.3.62f2c1\Editor\Data`），换机器需要改
- 脚本内的提示字符串一律用 ASCII：Windows PowerShell 5.1 按 ANSI 读取 `.ps1`，
  写中文会被解析坏（这个坑已经踩过一次）
- 它只能验证**编译**，不能验证**运行结果**。运行结果要看 Unity 控制台
