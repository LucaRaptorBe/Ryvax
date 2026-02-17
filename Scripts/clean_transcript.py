#!/usr/bin/env python3
"""
Clean and reformat YouTube transcript for Photon Quantum tutorial
"""

import re
import sys

def clean_transcript(input_file, output_file):
    """Clean up raw YouTube transcript"""

    with open(input_file, 'r', encoding='utf-8') as f:
        text = f.read()

    print(f"Original size: {len(text)} characters")

    # Step 1: Remove artifacts
    text = re.sub(r'\[Music\]', '', text, flags=re.IGNORECASE)
    text = re.sub(r'\[Applause\]', '', text, flags=re.IGNORECASE)
    text = re.sub(r'\[Laughter\]', '', text, flags=re.IGNORECASE)
    text = re.sub(r'\s+uh+\s+', ' ', text, flags=re.IGNORECASE)
    text = re.sub(r'\s+um+\s+', ' ', text, flags=re.IGNORECASE)

    # Step 2: Clean up spacing
    text = re.sub(r' +', ' ', text)
    text = text.strip()

    # Step 3: Add sentence breaks at natural pauses
    # Be more conservative - only break on strong signals

    # Add periods before "okay" when it clearly starts a new sentence
    text = re.sub(r'(\w)\s+okay\s+', r'\1. Okay ', text, flags=re.IGNORECASE)
    text = re.sub(r'(\w)\s+alright\s+', r'\1. Alright ', text, flags=re.IGNORECASE)

    # Add periods before "so" only when it starts a new major thought
    text = re.sub(r'(\w)\s+so\s+(let\'s|the\s+(?:first|next|idea)|this\s+is|we\'re\s+going|I\'m\s+going|here\'s|now\s+(?:let|the|we)|today|without)',
                  r'\1. So \2', text, flags=re.IGNORECASE)

    # Fix "here's" being split
    text = re.sub(r'\.\s+Here\'s\s+(?=\w)', r'. Here\'s ', text, flags=re.IGNORECASE)

    # Capitalize first letter
    if text:
        text = text[0].upper() + text[1:]

    # Step 4: Split into sentences
    sentences = re.split(r'(?<=[.!?])\s+', text)
    sentences = [s.strip() for s in sentences if s.strip()]

    # Step 5: Group sentences into paragraphs (4-7 sentences per paragraph)
    paragraphs = []
    current_para = []

    for sentence in sentences:
        current_para.append(sentence)

        # Break paragraph after 4-7 sentences
        should_break = False
        if len(current_para) >= 4:
            # Break on paragraph markers or when we have enough sentences
            starts_new_thought = any(sentence.lower().startswith(marker) for marker in [
                'okay', 'alright', 'so let\'s', 'so now', 'so the first', 'so the next', 'so without'
            ])

            if len(current_para) >= 7 or (len(current_para) >= 4 and starts_new_thought):
                should_break = True

        if should_break:
            paragraphs.append(' '.join(current_para))
            current_para = []

    # Add remaining sentences
    if current_para:
        paragraphs.append(' '.join(current_para))

    # Step 6: Detect major sections (be much more conservative)
    # Only create sections for clear, major topic transitions
    sections = []
    section_patterns = [
        # Match these patterns at the START of a paragraph for clear section breaks
        (r"^(?:Okay\.|Alright\.)?.*?let'?s\s+start\s+by\s+listing", "## Tutorial Overview"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:first\s+(?:thing|step)|where\s+do\s+you\s+download)", "## Installation and Download"),
        (r"^(?:Okay\.|Alright\.)?.*?quantum\s+is\s+a\s+(?:deterministic\s+)?game\s+engine", "## Quantum Architecture"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:what\s+are\s+)?entity\s+prototypes", "## Entity Prototypes"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:let'?s\s+(?:create|talk\s+about)|creating)\s+(?:custom\s+)?components", "## Custom Components"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:let'?s\s+(?:write|create|talk\s+about)|writing)\s+(?:a\s+)?(?:couple\s+of\s+)?systems", "## Systems"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:concept\s+of\s+a\s+|adding\s+a\s+|managing\s+)player", "## Players"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:define|defining|handling)\s+input", "## Input"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:test|testing)\s+(?:this\s+)?(?:online|multiplayer|networking)", "## Multiplayer Testing"),
        (r"^(?:Okay\.|Alright\.)?.*?configuration\s+(?:assets|files|settings)", "## Configuration"),
        (r"^(?:Okay\.|Alright\.)?.*?quantum\s+assets", "## Quantum Assets"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:new\s+)?view\s+framework", "## View Framework"),
        (r"^(?:Okay\.|Alright\.)?.*?(?:talk\s+about\s+)?events", "## Events"),
    ]

    current_section = {"title": "## Introduction", "content": []}

    for para in paragraphs:
        # Check if paragraph starts a new major section
        section_found = False
        for pattern, title in section_patterns:
            if re.search(pattern, para[:300], re.IGNORECASE):
                # Only create new section if we don't already have one with this title
                if not sections or sections[-1]["title"] != title:
                    # Save current section if it has content
                    if current_section["content"]:
                        sections.append(current_section)
                    # Start new section
                    current_section = {"title": title, "content": [para]}
                    section_found = True
                    break

        if not section_found:
            current_section["content"].append(para)

    # Add final section
    if current_section["content"]:
        sections.append(current_section)

    # If no sections detected, make it all one section
    if not sections:
        sections = [{"title": "## Content", "content": paragraphs}]

    # Step 7: Build output document
    output = "# Photon Quantum Tutorial - Part 1\n\n"
    output += "_Cleaned and formatted tutorial transcript_\n\n"
    output += "---\n\n"

    for section in sections:
        output += f"{section['title']}\n\n"

        for para in section['content']:
            # Final paragraph cleanup
            para = re.sub(r'\s+', ' ', para).strip()

            if len(para) > 20:  # Skip very short fragments
                output += para + "\n\n"

    # Final cleanup
    output = re.sub(r'\n\n\n+', '\n\n', output)
    output = output.strip() + '\n'

    # Write output
    with open(output_file, 'w', encoding='utf-8') as f:
        f.write(output)

    print(f"✓ Cleaned transcript written to {output_file}")
    print(f"  Cleaned size: {len(output)} characters")
    print(f"  Sections created: {len(sections)}")
    print(f"  Total paragraphs: {sum(len(s['content']) for s in sections)}")

if __name__ == '__main__':
    if len(sys.argv) != 3:
        print("Usage: python clean_transcript.py <input_file> <output_file>")
        sys.exit(1)

    clean_transcript(sys.argv[1], sys.argv[2])
