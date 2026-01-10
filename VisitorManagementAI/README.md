# AI Model Setup Guide (Phi-4-mini)

This module provides offline natural language querying using the **Phi-4-mini-instruct** ONNX model. Model artifacts are excluded from the repository due to their size (~5 GB) and must be provisioned during installation.

## 1. Prerequisites

To download the model, install the **Hugging Face CLI** via PowerShell:

```powershell
powershell -ExecutionPolicy ByPass -c "irm https://hf.co/cli/install.ps1 | iex"

```

## 2. Installation

Navigate to the `AI/Models/Phi-4-mini-cpu` directory and execute the following command:

```powershell
hf download microsoft/Phi-4-mini-instruct-onnx --include "cpu_and_mobile/cpu-int4-rtn-block-32-acc-level-4/*" --local-dir . --local-dir-use-symlinks False

```

## 3. Directory Structure

After the download is complete, verify that the files are organized as follows (flattened structure):

```text
AI/
└── Models/
    └── Phi-4-mini-cpu/
        ├── model.onnx
        ├── genai_config.json
        ├── tokenizer.json
        ├── tokenizer_config.json
        └── ...

```

## 4. Application Configuration

Update your `appsettings.json` to point to the current model directory:

```json
{
  "AiSettings": {
    "ModelPath": "AI/Models/Phi-4-mini-cpu"
  }
}

```

## 5. Technical Requirements

* **Runtime**: Fully offline inference (no internet required).
* **RAM**: 8 GB minimum (16 GB recommended).
* **OS**: Windows and Linux (Ubuntu) compatible.

---