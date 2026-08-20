import os
import sys
import subprocess
from pathlib import Path

from huggingface_hub import hf_hub_download


# ============================================================
# Miao - Setup CTranslate2 translation model
# Model: Helsinki-NLP/opus-mt-zh-vi
# CTranslate2: 4.8.1
# Quantization: int8
# ============================================================

MODEL_NAME = "Helsinki-NLP/opus-mt-zh-vi"

BASE_DIR = Path(__file__).resolve().parent
MODEL_DIR = BASE_DIR / "ct2_model"


def run_command(command):
    print()
    print(">>>", " ".join(command))

    result = subprocess.run(command)

    if result.returncode != 0:
        print()
        print("ERROR: Command failed.")
        sys.exit(result.returncode)


def check_ct2_version():
    try:
        import ctranslate2
    except ImportError:
        print("CTranslate2 chưa được cài.")
        print()
        print("Chạy:")
        print("    pip install ctranslate2==4.8.1")
        sys.exit(1)

    version = ctranslate2.__version__

    print(f"CTranslate2 version: {version}")

    if version != "4.8.1":
        print()
        print(
            "WARNING: Miao đang dùng CTranslate2 4.8.1 "
            f"nhưng môi trường hiện tại là {version}."
        )
        print("Khuyến nghị:")
        print("    pip install --upgrade --force-reinstall ctranslate2==4.8.1")
        print()


def download_sentencepiece_files():
    print()
    print("Downloading SentencePiece files...")
    print(f"Model: {MODEL_NAME}")

    MODEL_DIR.mkdir(parents=True, exist_ok=True)

    source_spm = hf_hub_download(
        repo_id=MODEL_NAME,
        filename="source.spm"
    )

    target_spm = hf_hub_download(
        repo_id=MODEL_NAME,
        filename="target.spm"
    )

    source_destination = MODEL_DIR / "source.spm"
    target_destination = MODEL_DIR / "target.spm"

    # Copy từ Hugging Face cache sang ct2_model
    source_destination.write_bytes(
        Path(source_spm).read_bytes()
    )

    target_destination.write_bytes(
        Path(target_spm).read_bytes()
    )

    print(f"Downloaded: {source_destination}")
    print(f"Downloaded: {target_destination}")


def convert_model():
    print()
    print("Converting Hugging Face model to CTranslate2...")
    print()

    MODEL_DIR.mkdir(parents=True, exist_ok=True)

    command = [
        sys.executable,
        "-m",
        "ctranslate2.converters.transformers",

        "--model",
        MODEL_NAME,

        "--output_dir",
        str(MODEL_DIR),

        "--quantization",
        "int8",
    ]

    run_command(command)


def verify_model():
    print()
    print("Checking generated files...")
    print()

    required_files = [
        MODEL_DIR / "config.json",
        MODEL_DIR / "model.bin",
        MODEL_DIR / "shared_vocabulary.json",
        MODEL_DIR / "source.spm",
        MODEL_DIR / "target.spm",
    ]

    success = True

    for file in required_files:
        if file.exists():
            size_mb = file.stat().st_size / (1024 * 1024)
            print(f"[OK] {file.name:<25} {size_mb:.2f} MB")
        else:
            print(f"[MISSING] {file.name}")
            success = False

    if not success:
        print()
        print("Model setup chưa hoàn tất.")
        sys.exit(1)

    print()
    print("Model setup thành công.")


def main():
    print("=" * 60)
    print("MIAO - Translation Model Setup")
    print("=" * 60)

    print()
    print(f"Model       : {MODEL_NAME}")
    print("CTranslate2 : 4.8.1")
    print("Quantization: int8")
    print(f"Output      : {MODEL_DIR}")

    check_ct2_version()

    download_sentencepiece_files()

    convert_model()

    verify_model()

    print()
    print("=" * 60)
    print("DONE")
    print("=" * 60)
    print()
    print("Các file model nằm tại:")
    print(MODEL_DIR)
    print()
    print("Miao TranslateServer có thể sử dụng model này.")
    print()


if __name__ == "__main__":
    main()

