# Local command approval

Pinned upstream LANCET Nano v0.4.3: https://huggingface.co/fingerthief/lancet-nano/tree/2450cfbea514baef810f4087d6d31854783f3d2e/bundle

Weights and the MIT reference classifier are downloaded only after the user accepts installation. Every file is checked against the embedded manifest. Upstream Apache-2.0 model and MIT runtime notices accompany the installation. Commands are inert JSON text; the classifier never executes them.

CPU Python dependencies use ONNX Runtime 1.23.2, NumPy 2.2.6 and Tokenizers 0.22.2 to retain Python 3.13 wheels for macOS Intel and Apple Silicon, Linux and Windows. This differs from the newer upstream requirements, which no longer include macOS Intel. Runtime preparation verifies loading before marking the installation complete; failures require manual approval.

Only `not_flagged` with finite model output may proceed automatically. `review`, `risky`, unknown shells, oversized inputs, missing model, timeout or runtime errors request a one-time human decision. Saved scope grants never bypass a flagged command in Automatic mode. Project denials and explicit Ask rules remain authoritative.
