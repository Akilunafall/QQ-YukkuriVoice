# Third-Party Notices

This software is an independent fan utility. It is not an official QQ, Touhou
Project, Microsoft, AQUEST, or VB-Audio product.

## NAudio 2.2.1

Copyright (c) 2020 Mark Heath. MIT License.

Source: https://github.com/naudio/NAudio

## Pinyin4net-core 1.1.0

Pinyin4net converts Chinese characters to Hanyu Pinyin. It is used only for
the local Chinese-to-kana preprocessing step.

Copyright (c) Yang Kuang. MIT License.

Source: https://github.com/YangKuang/pinyin4net

## .NET runtime

Copyright (c) .NET Foundation and Contributors. MIT License.

Source: https://github.com/dotnet/runtime

The portable distribution includes Microsoft .NET and Windows Desktop runtime
components. Applicable Microsoft binary distribution terms and third-party
notices are included under the distribution's `licenses` directory.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## AquesTalk

AquesTalk is a product of AQUEST Corporation. This distribution includes the
unmodified legacy AquesTalk1 DLL packages under `Assets/voices/f1`, `f2`, `m1`,
`m2`, `dvd`, `imd1`, `jgr`, and `r1`, together with the original `AqLicence.txt` in each
directory. The packages are redistributed under the AQUEST terms included
with those files. The software does not modify the DLLs.

Official product and licensing information:
https://www.a-quest.com/products/aquestalk_1.html
https://www.a-quest.com/licence.html

The Chinese-to-kana mapping is based on the converter data published by the
zh-yukkuri-offline project:
https://github.com/Love-Kogasa/zh-yukkuri-offline

Official evaluation SDKs (AquesTalk1 2.0.1, AquesTalk2 2.4.0, AquesTalk10 1.1.0)
downloaded by the local user are stored separately under
`%LOCALAPPDATA%/YukkuriVoice/Engines`. They are not included in the portable
distribution. They retain the official restrictions and original filenames.
No patched library or license bypass is used.

## Local QQ integration

Version 1.2 removes PMHQ and LuckyLillia as runtime dependencies. The local
connector is built from this project's source and uses the running QQ process.
It does not redistribute Tencent QQ files or bypass a third-party service's
authentication. Unofficial QQ integrations can break with client updates and
may trigger the client's security policies or account restrictions.

### MinHook 1.3.4

Copyright (C) 2009-2017 Tsuda Kageyu. BSD 2-Clause license, including the
upstream HDE notices, is supplied in `Assets/qq-bridge/MinHook-LICENSE.txt`.
Source: https://github.com/TsudaKageyu/minhook/tree/v1.3.4
Pinned source ZIP SHA-256:
172708123daa0c98d20d3a980b16a50be14af243dc95dee6f79c24193ad010e4.

### silk-wasm 3.7.1

Copyright (c) 2024 idranme. MIT License. Unmodified `index.cjs` and `silk.wasm`
are supplied under `Assets/qq-bridge/silk`, with their original license text.
Source: https://github.com/idranme/silk-wasm
This release uses the files from the previously downloaded local dependency;
no external package manager or encoder service is required by the end user.

Reference research: the MIT-licensed WhaleBridge project documents QQ's IPC
envelope. This project's runtime is a dedicated voice-only connector; it does
not include WhaleBridge's general-purpose HTTP server or injector binaries.
Source: https://github.com/lon960/whalebridge

## Touhou Project

Touhou Project and its characters belong to their respective rights holders,
including Team Shanghai Alice. This program uses technical AquesTalk voice
labels and does not imply endorsement or official character voice models.
