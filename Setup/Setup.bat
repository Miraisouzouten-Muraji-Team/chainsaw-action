@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem Unity Team Project - Git Initial Setup
rem ============================================================
rem
rem Clone後に1回だけ実行する。
rem
rem 設定内容:
rem   1. .gitmessage をコミットテンプレートとして設定
rem   2. .githooks をGit Hooksの共有フォルダとして設定
rem   3. 改行コード変換を .gitattributes に任せる
rem   4. Git LFSをこのRepositoryで有効化
rem
rem すべて --local 設定なので、
rem 他のRepositoryには影響しない。
rem ============================================================


rem ------------------------------------------------------------
rem Repository Rootを取得
rem
rem setup.bat は
rem RepositoryRoot\setup\setup.bat
rem に置く前提。
rem ------------------------------------------------------------

for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"

cd /d "%REPO_ROOT%"


echo.
echo ========================================
echo Unity Project Git Setup
echo ========================================
echo.
echo Repository:
echo %REPO_ROOT%
echo.


rem ============================================================
rem 1. Gitが使用可能か確認
rem ============================================================

where git >nul 2>&1

if errorlevel 1 (
    echo [ERROR] Gitが見つかりません。
    echo.
    echo GitまたはForkのGit環境を確認してください。
    echo.
    pause
    exit /b 1
)

echo [OK] Git
echo.


rem ============================================================
rem 2. Git Repositoryか確認
rem ============================================================

git rev-parse --is-inside-work-tree >nul 2>&1

if errorlevel 1 (
    echo [ERROR] Git Repositoryではありません。
    echo.
    echo ForkなどでRepositoryをCloneしてから
    echo このbatを実行してください。
    echo.
    pause
    exit /b 1
)

echo [OK] Git Repository
echo.


rem ============================================================
rem 3. 必要な共有設定ファイルを確認
rem ============================================================

if not exist ".gitmessage" (
    echo [ERROR] .gitmessage が見つかりません。
    echo.
    echo Repository Rootに
    echo .gitmessage を配置してください。
    echo.
    pause
    exit /b 1
)

if not exist ".gitattributes" (
    echo [ERROR] .gitattributes が見つかりません。
    echo.
    echo Repository Rootに
    echo .gitattributes を配置してください。
    echo.
    pause
    exit /b 1
)

if not exist ".githooks" (
    echo [ERROR] .githooks フォルダが見つかりません。
    echo.
    pause
    exit /b 1
)

if not exist ".githooks\commit-msg" (
    echo [ERROR] .githooks\commit-msg が見つかりません。
    echo.
    pause
    exit /b 1
)

echo [OK] Shared Git Files
echo.


rem ============================================================
rem 4. Commit Message Template
rem ============================================================
rem
rem ForkなどからCommitするときに
rem Repository Rootの.gitmessageを表示する。
rem ============================================================

git config --local commit.template ".gitmessage"

if errorlevel 1 (
    echo [ERROR] Commit Templateの設定に失敗しました。
    echo.
    pause
    exit /b 1
)

echo [OK] Commit Template
echo      .gitmessage
echo.


rem ============================================================
rem 5. Git Hooks
rem ============================================================
rem
rem 通常Gitは .git/hooks を使用する。
rem
rem チームでHooksを共有できるよう、
rem Repository管理されている .githooks を使用する。
rem ============================================================

git config --local core.hooksPath ".githooks"

if errorlevel 1 (
    echo [ERROR] Git Hooksの設定に失敗しました。
    echo.
    pause
    exit /b 1
)

echo [OK] Git Hooks
echo      .githooks
echo.


rem ============================================================
rem 6. 改行コード設定
rem ============================================================
rem
rem Windows Gitではcore.autocrlf=trueになっている場合がある。
rem
rem このRepositoryでは.gitattributes側で
rem
rem *.cs  text eol=lf
rem *.bat text eol=crlf
rem
rem のように改行コードを管理する。
rem
rem そのためGit側による自動変換は無効にする。
rem ============================================================

git config --local core.autocrlf false

if errorlevel 1 (
    echo [ERROR] core.autocrlf の設定に失敗しました。
    echo.
    pause
    exit /b 1
)

echo [OK] Line Ending
echo      core.autocrlf = false
echo.


rem ============================================================
rem 7. Git LFS
rem ============================================================
rem
rem .gitattributesで
rem
rem *.fbx filter=lfs ...
rem *.wav filter=lfs ...
rem
rem などを使用する場合に必要。
rem
rem Git Hooksは.githooksで共有管理するため、
rem git lfs installにはHookを書き換えさせない。
rem ============================================================

git lfs version >nul 2>&1

if errorlevel 1 (
    echo [WARNING] Git LFSが見つかりません。
    echo.
    echo LFS対象ファイルを使用する場合は
    echo Git LFSをインストールしてください。
    echo.
) else (
    git lfs install --local --skip-repo

    if errorlevel 1 (
        echo [WARNING] Git LFSの設定に失敗しました。
        echo.
    ) else (
        echo [OK] Git LFS
        echo.
    )
)


rem ============================================================
rem 8. 設定結果表示
rem ============================================================

echo.
echo ========================================
echo Setup Complete
echo ========================================
echo.

echo Commit Template:
git config --local --get commit.template

echo.
echo Git Hooks:
git config --local --get core.hooksPath

echo.
echo core.autocrlf:
git config --local --get core.autocrlf

echo.
echo ----------------------------------------
echo 初回セットアップが完了しました。
echo ----------------------------------------
echo.
echo Forkを起動済みの場合は、
echo 一度終了して再起動してください。
echo.

pause

endlocal