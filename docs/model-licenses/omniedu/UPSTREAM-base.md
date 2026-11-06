---
library_name: transformers
license: other
base_model: Qwen/Qwen3.8-27B
tags:
- education
- k-12
- tutoring
- curriculum-grounding
- multimodal
- instruction-tuning
- llama-factory
- full
- arxiv:2609.23088
model-index:
- name: OmniEdu-27B
  results: []
pipeline_tag: image-text-to-text
base_model_relation: finetune
datasets:
- lhpku20010120/Omni-Edu
---

# OmniEdu-27B

**Open Foundation Models for Learning and Teaching**

[Paper](https://arxiv.org/abs/2609.23088) · [Project Page](https://haolpku.github.io/Omni-Edu/) · [GitHub](https://github.com/haolpku/Omni-Edu) · [Dataset](https://huggingface.co/datasets/lhpku20010120/Omni-Edu) · [Model Family](#model-family) · [Citation](#citation)

**OmniEdu-27B** is a multimodal model for K–12 learning and teaching, fine-tuned from [Qwen/Qwen3.8-27B](https://huggingface.co/Qwen/Qwen3.8-27B). It connects subject knowledge, curriculum understanding, learner diagnosis, and instructional support. It is the largest checkpoint in the family, with the strongest overall educational results among the three OmniEdu scales reported in the paper.

This checkpoint accompanies [**OmniEdu: Open Foundation Models for Learning and Teaching**](https://arxiv.org/abs/2609.23088).

Hao Liang · Qihan Lin · Meiyi Qiang · Linzhuang Sun · Hengyi Feng · Mingrui Chen · Sizhe Qiu · Wentao Zhang  
Peking University · University of the Chinese Academy of Sciences · Zhongguancun Academy

## Capabilities

An educational model needs to connect **what is being taught, what the learner understands, and what to do next**. OmniEdu organizes supervised fine-tuning around four complementary capabilities:

| Capability | What the model is trained to do |
| --- | --- |
| Subject competence | Solve K–12 problems across subjects and input formats |
| Curriculum grounding | Link questions, concepts, and solutions to curriculum standards and prerequisites |
| Diagnostic reasoning | Identify misconceptions, missing prerequisites, and gaps in learner understanding |
| Pedagogical action and scaffolding | Give targeted feedback, ask guiding questions, explain, and adapt instructional support |

The training mixture contains **69,999 instruction examples** and **15.96M supervised response tokens**, drawn from more than 100 sources: **60,951 education-specific examples** and **9,048 general-purpose examples**, with **20 task-specific system instructions**. The models use full-parameter supervised fine-tuning with a **32,768-token training sequence length**.

## Model family

| Checkpoint | Scale | Backbone reported in the paper |
| --- | ---: | --- |
| [OmniEdu-4B](https://huggingface.co/lhpku20010120/Omni-Edu-4B) | 4B | Qwen3.5-4B-Base |
| [OmniEdu-9B](https://huggingface.co/lhpku20010120/Omni-Edu-9B) | 9B | Qwen3.5-9B-Base |
| [OmniEdu-27B](https://huggingface.co/lhpku20010120/Omni-Edu-27B) | 27B | Qwen3.8-27B |

This repository contains **OmniEdu-27B**. All three published checkpoints use the `Qwen3_5ForConditionalGeneration` architecture. Use the multimodal model class and processor shown below, including for text-only conversations.

## Evaluation

The following results are reported in [Tables 1–3 of the paper (arXiv v1)](https://arxiv.org/html/2609.23088v1#S4). The current checkpoint's column is highlighted. Higher is better; all values are percentages except LongTutor Teaching, which uses the benchmark's original score scale. WR means win rate.

| Metric ↑ | OmniEdu-4B | OmniEdu-9B | **OmniEdu-27B** |
| --- | --- | --- | --- |
| K12-Bench EM | 54.25% | 55.46% | **63.12%** |
| K12-Bench F1 | 71.75% | 73.68% | **76.69%** |
| MathFish Acc. | 83.19% | 83.70% | **85.89%** |
| EDUMATH MaC | 68.40% | 74.00% | **86.95%** |
| GAOKAO-Bench Full | 90.46% | 93.66% | **94.87%** |
| EXAMS-V Overall | 57.62% | 66.40% | **69.52%** |
| MDK12-Bench Full | 46.36% | 50.80% | **57.76%** |
| MathTutorBench Scaffold WR | 75.79% | 75.26% | **78.74%** |
| MathTutorBench Scaffold-hard WR | 84.77% | 81.64% | **83.59%** |
| TutorBench Overall | 46.67% | 48.16% | **59.42%** |
| LongTutor Evidence | 65.88% | 66.63% | **78.20%** |
| LongTutor Teaching | 2.29 | 2.66 | **3.02** |

OmniEdu-27B obtains the highest K12-Bench EM/F1, MathFish accuracy, and LongTutor Teaching average among the 16 models evaluated in the paper. Comparisons follow the paper's evaluation protocol; see the [paper](https://arxiv.org/abs/2609.23088) and [full comparison tables](https://github.com/haolpku/Omni-Edu#full-education-comparisons) for baselines, input settings, and general-capability results.

## Quick start

### Transformers

Use a recent Transformers release with Qwen3.5 support and a GPU setup with sufficient memory for the chosen checkpoint. The example below loads this checkpoint.

```bash
pip install -U torch torchvision transformers accelerate safetensors pillow
```

```python
import torch
from transformers import AutoProcessor, Qwen3_5ForConditionalGeneration

MODEL_ID = "lhpku20010120/Omni-Edu-27B"
processor = AutoProcessor.from_pretrained(MODEL_ID)
model = Qwen3_5ForConditionalGeneration.from_pretrained(
    MODEL_ID,
    dtype=torch.bfloat16,
    device_map="auto",
).eval()

messages = [
    {
        "role": "user",
        "content": [
            {
                "type": "text",
                "text": "A Grade 7 student says summer is warmer because Earth is closer to the Sun. Identify the misconception and give a guiding question before explaining.",
            }
        ],
    }
]
inputs = processor.apply_chat_template(
    messages,
    add_generation_prompt=True,
    tokenize=True,
    return_dict=True,
    return_tensors="pt",
    enable_thinking=False,
).to(model.device)

with torch.inference_mode():
    outputs = model.generate(**inputs, max_new_tokens=512, do_sample=False)

answer = processor.batch_decode(
    outputs[:, inputs["input_ids"].shape[-1]:],
    skip_special_tokens=True,
)[0]
print(answer)
```

For image-based questions, include an image item such as `{"type": "image", "image": "/path/to/question.png"}` alongside the text item in `content`. The processor prepares both modalities. See the [official Qwen3.5 Transformers documentation](https://huggingface.co/docs/transformers/model_doc/qwen3_5) for supported input formats.

**Chat template:** Training uses `qwen3_5_nothink`. Keep `enable_thinking=False` when formatting prompts to match the training setup. The 32,768-token length reported above is the training sequence length, not a measured guarantee of long-context performance.

For an OpenAI-compatible deployment, see the [vLLM example in the project repository](https://github.com/haolpku/Omni-Edu#2-serve-an-openai-compatible-api-with-vllm). Set the model ID to `lhpku20010120/Omni-Edu-27B` and choose GPU parallelism and context length for your available memory.

### Prompting for educational tasks

State the learner's grade, topic, prior attempt, and the kind of support you want. For example:

- **Activity planning:** “Design a 30-minute Grade 7 technology activity about electrical circuits using batteries, wires, and LEDs. Include learning objectives and questions to check understanding.”
- **Learner diagnosis:** “A student says 1/8 is greater than 1/6 because 8 is greater than 6. Identify the misconception and suggest a question that helps them reconsider.”
- **Guided tutoring:** “Help me solve x² − 5x + 6 = 0. Give one hint at a time and wait for my response.”

## Training data

Load the released instruction mixture from [Hugging Face](https://huggingface.co/datasets/lhpku20010120/Omni-Edu):

```bash
pip install -U datasets
```

```python
from datasets import load_dataset

train = load_dataset(
    "lhpku20010120/Omni-Edu",
    "core_v6_full_system_prompted",
    split="train",
)
print(train)
```

The dataset covers curriculum grounding, problem solving, diagnosis, tutoring, and general instruction. Please follow the licenses and usage terms of the component datasets and source materials.

## Training details

Full-parameter supervised fine-tuning uses LLaMA-Factory, BF16 precision, and the `qwen3_5_nothink` template, with a maximum training sequence length of 32,768 tokens. See the [paper's training configuration](https://arxiv.org/html/2609.23088v1) for the full setup.

<details>
<summary>Checkpoint training hyperparameters and framework versions</summary>

The following hyperparameters were used during training:
- learning_rate: 5e-06
- train_batch_size: 1
- eval_batch_size: 8
- seed: 42
- distributed_type: multi-GPU
- num_devices: 16
- gradient_accumulation_steps: 8
- total_train_batch_size: 128
- total_eval_batch_size: 128
- optimizer: Use OptimizerNames.ADAMW_TORCH_FUSED with betas=(0.9,0.999) and epsilon=1e-08 and optimizer_args=No additional optimizer arguments
- lr_scheduler_type: cosine
- lr_scheduler_warmup_steps: 0.1
- num_epochs: 3.0

### Framework versions

- Transformers 5.2.0
- Pytorch 2.10.0
- Datasets 4.0.0
- Tokenizers 0.22.2

</details>

## Intended use and limitations

OmniEdu is intended for educational research, teacher-assistance tools, and learning-support prototypes. Models may produce incorrect solutions, unsupported curriculum mappings, or unsuitable instructional guidance. Educators should review generated material before using it with learners. Benchmark results do not establish improvements in classroom learning outcomes, and performance can vary by subject, curriculum, language, and image quality.

## Citation

If you use OmniEdu models or data in your research, please cite:

```bibtex
@misc{liang2026omniedu,
  title         = {OmniEdu: Open Foundation Models for Learning and Teaching},
  author        = {Hao Liang and Qihan Lin and Meiyi Qiang and Linzhuang Sun and Hengyi Feng and Mingrui Chen and Sizhe Qiu and Wentao Zhang},
  year          = {2026},
  eprint        = {2609.23088},
  archivePrefix = {arXiv},
  primaryClass  = {cs.CL},
  url           = {https://arxiv.org/abs/2609.23088}
}
```

## Feedback

We welcome classroom use cases, bug reports, and community contributions. Please open a discussion in [this model's Community tab](https://huggingface.co/lhpku20010120/Omni-Edu-27B/discussions) or an [issue in the project repository](https://github.com/haolpku/Omni-Edu/issues).
