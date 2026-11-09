#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""NineRE Launcher构建：Roslyn csc + .NET Framework 4.8 参考程序集 → dist\\PalrLauncher.exe

用法：python tools/launcher/build.py
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REF = r'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
CSC_CANDIDATES = [
    r'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    r'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
    r'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\Roslyn\csc.exe',
    r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe',
]

SOURCES = ['Program.cs', 'Paths.cs', 'Store.cs', 'Stats.cs', 'Services.cs',
           'Launch.cs', 'Theme.cs', 'GamePrefs.cs', 'ScrollStyle.cs', 'Dialogs.cs', 'DialogsExtra.cs', 'MainWindow.cs']

REFS = ['mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xaml.dll',
        'WindowsBase.dll', 'PresentationCore.dll', 'PresentationFramework.dll']


def main():
    csc = next((p for p in CSC_CANDIDATES if os.path.exists(p)), None)
    if csc is None:
        print('找不到 csc.exe')
        return 1
    out_dir = os.path.join(HERE, 'dist')
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, 'PalrLauncher.exe')

    ico = os.path.join(HERE, 'app.ico')
    cmd = [csc, '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
           '/langversion:latest', '/win32manifest:' + os.path.join(HERE, 'app.manifest'),
           '/out:' + out]
    if os.path.exists(ico):
        cmd.append('/win32icon:' + ico)
    cmd += ['/r:' + os.path.join(REF, r) for r in REFS]
    cmd += [os.path.join(HERE, s) for s in SOURCES]

    print('csc:', csc)
    r = subprocess.run(cmd, capture_output=True, text=True, encoding='utf-8', errors='replace')
    if r.stdout.strip():
        print(r.stdout.strip())
    if r.returncode != 0:
        if r.stderr.strip():
            print(r.stderr.strip())
        print('构建失败')
        return r.returncode
    size = os.path.getsize(out)
    print('构建完成: %s (%d KB)' % (out, size // 1024))
    return 0


if __name__ == '__main__':
    sys.exit(main())
