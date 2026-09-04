@echo off
setlocal EnableExtensions

rem ============================================================
rem Unity Team Project - Git Initial Setup
rem ============================================================
rem
rem Clone後に1回だけ実行する。
rem
rem 設定内容:
rem   1. .gitmessage をコミットテンプレートとして設定
rem   2. 改行コード変換を .gitattributes に任せる
rem   3. Git LFSをこのRepositoryで有効化
rem
rem 設定は --local なので、
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
rem 1. Git Repositoryか確認
rem ============================================================

if not exist ".git" (
    echo [ERROR] .git が見つかりません。
    echo.
    echo ForkなどでRepositoryをCloneしてから
    echo このbatを実行してください。
    echo.
    pause
    exit /b 1
)


rem ============================================================
rem 2. Gitが使用可能か確認
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
rem 3. Commit Message Template
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

rem このRepositoryだけで.gitmessageを使用する。
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
rem 4. 改行コード設定
rem ============================================================
rem
rem Windows Gitではcore.autocrlf=trueになっている場合がある。
rem
rem このプロジェクトでは.gitattributes側で
rem
rem *.cs text eol=lf
rem
rem のように改行コードを管理するため、
rem Git側の自動CRLF変換を無効にする。
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
rem 5. Git LFS
rem ============================================================
rem
rem .gitattributesで
rem
rem *.fbx filter=lfs ...
rem *.psd filter=lfs ...
rem
rem などを使用する場合に必要。
rem ============================================================

git lfs version >nul 2>&1

if errorlevel 1 (
    echo [WARNING] Git LFSが見つかりません。
    echo.
    echo LFS対象ファイルを使用する場合は
    echo Git LFSをインストールしてください。
    echo.
) else (

    rem --local により、このRepositoryだけにLFS設定を適用する。
    git lfs install --local >nul 2>&1

    if errorlevel 1 (
        echo [WARNING] Git LFSの設定に失敗しました。
        echo.
    ) else (
        echo [OK] Git LFS
        echo.
    )
)


rem ============================================================
rem 6. 設定結果表示
rem ============================================================

echo ========================================
echo Setup Complete
echo ========================================
echo.

echo Commit Template:
git config --local --get commit.template

echo.
echo core.autocrlf:
git config --local --get core.autocrlf

echo.
echo 初回セットアップが完了しました。
echo Forkを起動済みの場合は、
echo 一度再起動してください。
echo.

pause