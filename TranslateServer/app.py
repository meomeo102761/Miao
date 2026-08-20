from flask import Flask, request, jsonify
import re
import ctranslate2
import sentencepiece as spm
from huggingface_hub import hf_hub_download

app = Flask(__name__)

print("Đang tải model CTranslate2...")

translator = ctranslate2.Translator(
    "ct2_model",
    device="cpu"
)

source_spm_path = hf_hub_download(
    repo_id="Helsinki-NLP/opus-mt-zh-vi",
    filename="source.spm"
)

target_spm_path = hf_hub_download(
    repo_id="Helsinki-NLP/opus-mt-zh-vi",
    filename="target.spm"
)

sp_source = spm.SentencePieceProcessor(
    model_file=source_spm_path
)

sp_target = spm.SentencePieceProcessor(
    model_file=target_spm_path
)

print("Model đã tải xong, server sẵn sàng.")


@app.route("/health", methods=["GET"])
def health():
    return jsonify({"status": "ok"})


def cleanup_repetition(text: str) -> str:
    """Chỉ xử lý các lỗi lặp token rõ ràng do model sinh ra."""
    if not text:
        return text

    # Lặp cùng một từ 3 lần trở lên.
    text = re.sub(
        r"(?iu)(\b[^\s，。！？!?；;：:、]+)(?:\s+\1){2,}",
        r"\1",
        text,
    )

    # Một số từ tên riêng thường bị lặp 2 lần liên tiếp.
    suspicious_words = {
        "hải", "du", "đường", "tuyết", "hùng", "lộ", "star"
    }

    def collapse_suspicious(match: re.Match[str]) -> str:
        word = match.group(1)
        return word if word.lower() in suspicious_words else match.group(0)

    text = re.sub(
        r"(?iu)(\b[^\s，。！？!?；;：:、]+)\s+\1\b",
        collapse_suspicious,
        text,
    )

    # Lặp nguyên cụm ngắn 2-4 từ.
    words = text.split()
    if len(words) >= 4:
        cleaned = []
        i = 0
        while i < len(words):
            collapsed = False

            for size in range(min(4, (len(words) - i) // 2), 1, -1):
                first = words[i:i + size]
                second = words[i + size:i + size * 2]

                if (
                    len(second) == size and
                    [w.lower() for w in first] == [w.lower() for w in second]
                ):
                    cleaned.extend(first)
                    i += size * 2
                    collapsed = True
                    break

            if not collapsed:
                cleaned.append(words[i])
                i += 1

        text = " ".join(cleaned)

    return text


@app.route("/translate", methods=["POST"])
def translate():
    data = request.get_json() or {}
    text = data.get("text", "")

    if not text.strip():
        return jsonify({"translation": ""})

    tokens = sp_source.encode(
        text,
        out_type=str
    )

    # Marian/OPUS-MT cần token kết thúc câu khi tự tokenize bằng
    # SentencePiece trước khi đưa vào CTranslate2.
    if not tokens or tokens[-1] != "</s>":
        tokens.append("</s>")

    # Các chunk mới dài hơn để giữ ngữ cảnh, nên cần cửa sổ decoding rộng hơn.
    max_decoding_length = max(
        96,
        min(768, len(tokens) * 3 + 64)
    )

    results = translator.translate_batch(
        [tokens],
        beam_size=6,
        max_decoding_length=max_decoding_length,
        repetition_penalty=1.15,
        no_repeat_ngram_size=3
    )

    output_tokens = results[0].hypotheses[0]
    translated = sp_target.decode(output_tokens)
    translated = cleanup_repetition(translated)

    return jsonify({
        "translation": translated
    })


if __name__ == "__main__":
    app.run(
        host="0.0.0.0",
        port=5001
    )
