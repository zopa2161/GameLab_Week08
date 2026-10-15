"""설치된 Python과 분석 도구를 배포 폴더의 상대 경로 실행 환경으로 복사한다."""

import argparse
import shutil
import site
import sys
from pathlib import Path


def main():
    """<summary>원본 앱 폴더 인수를 받아 실행 환경·라이선스·앱을 신규 배포 디렉터리에 구성한다.</summary>"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--app-source", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    runtime = root / "runtime"
    app = root / "app"
    base = Path(sys.base_prefix)
    packages = next(Path(path) for path in site.getsitepackages()
                    if Path(path, "streamlit").is_dir())
    if runtime.exists() or app.exists():
        raise SystemExit("runtime/app already exists. Build into a fresh directory to preserve existing files.")
    if sys.version_info[:2] != (3, 12) or sys.platform != "win32":
        raise SystemExit("Build with a Windows x64 Python 3.12 environment containing the dashboard dependencies.")
    for name in ("app.py", "analysis.py", "requirements.txt"):
        if not (args.app_source / name).is_file():
            raise SystemExit(f"Missing app source: {name}")
    runtime.mkdir()
    for name in ("python.exe", "pythonw.exe", "python3.dll", "python312.dll",
                 "vcruntime140.dll", "vcruntime140_1.dll", "LICENSE.txt"):
        shutil.copy2(base / name, runtime / name)
    excluded = shutil.ignore_patterns("site-packages", "__pycache__", "*.pyc", "test", "tests", "ensurepip")
    shutil.copytree(base / "Lib", runtime / "Lib", ignore=excluded)
    shutil.copytree(base / "DLLs", runtime / "DLLs", ignore=excluded)
    shutil.copytree(packages, runtime / "Lib" / "site-packages",
                    ignore=shutil.ignore_patterns("__pycache__", "*.pyc", "test", "tests", "pip", "pip-*.dist-info"))
    (runtime / "python312._pth").write_text(".\nLib\nDLLs\nLib/site-packages\n../app\nimport site\n", encoding="ascii")
    app.mkdir()
    for name in ("app.py", "analysis.py", "requirements.txt"):
        shutil.copy2(args.app_source / name, app / name)
    source = (app / "app.py").read_text(encoding="utf-8")
    original = '    example = st.checkbox("첨부했던 예제 데이터로 보기", value=not bool(uploads), disabled=bool(uploads))'
    replacement = '    example = False'
    if source.count(original) != 1:
        raise SystemExit("Example checkbox changed; review portable app generation before distributing.")
    source = source.replace(original, replacement)
    source = source.replace(r"C:\Users\JUNGLE\Desktop\로그", r"C:\PlayLogs")
    (app / "app.py").write_text(source, encoding="utf-8")
    size = sum(path.stat().st_size for path in root.rglob("*") if path.is_file())
    print(f"Portable runtime created: {root} ({size / 1024 ** 2:.1f} MiB)")


if __name__ == "__main__":
    main()
