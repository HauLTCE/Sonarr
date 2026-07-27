#!/usr/bin/env sh
# Fetch the embedding model for the semantic tier (docs/03-stack.md).
#
# sentence-transformers/all-MiniLM-L6-v2, fp32 ONNX: 384-dim, ~90MB, ~4ms/embed.
# The quantized variants in that repo are all AVX2/AVX512/ARM64 builds and the J2900
# is SSE4.2-only, so fp32 is the portable choice — and it already fits the budget.
#
# ~90MB of weights do not belong in git (models/ is gitignored). Run this once per
# machine. If you skip it the bot still runs; matching stays lexical-only.
set -eu

REPO=https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main
DEST="${1:-$(dirname "$0")/../models/minilm-l6-v2}"

mkdir -p "$DEST"
fetch() {
    if [ -s "$DEST/$2" ]; then
        echo "have $2"
        return
    fi
    echo "fetching $2..."
    # -f so a 404 fails the script instead of leaving an HTML error page named model.onnx.
    curl -fL --progress-bar -o "$DEST/$2.part" "$REPO/$1"
    mv "$DEST/$2.part" "$DEST/$2"
}

fetch onnx/model.onnx model.onnx
fetch vocab.txt vocab.txt

echo "model ready in $DEST — set MODEL_PATH to it (see .env.example)"
