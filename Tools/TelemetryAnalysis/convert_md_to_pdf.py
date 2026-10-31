import os
import re
import sys
import subprocess
import markdown

EDGE_PATH = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

def render_markdown_to_html(md_text, title="Group 15 Technical Dossier"):
    # Pre-process GitHub alerts: > [!NOTE] or > [!IMPORTANT]
    def replace_alerts(match):
        alert_type = match.group(1).upper()
        content = match.group(2)
        css_class = "alert-note"
        title_text = "NOTE"
        if alert_type == "IMPORTANT":
            css_class = "alert-important"
            title_text = "IMPORTANT CLINICAL NOTE"
        elif alert_type == "WARNING":
            css_class = "alert-warning"
            title_text = "WARNING"
        elif alert_type == "TIP":
            css_class = "alert-tip"
            title_text = "KEY TAKEAWAY"
        
        # Clean lines
        lines = [re.sub(r'^>\s?', '', l) for l in content.strip().split('\n')]
        inner_html = markdown.markdown('\n'.join(lines), extensions=['extra', 'tables'])
        return f'<div class="alert {css_class}"><strong>{title_text}</strong>{inner_html}</div>'

    alert_pattern = re.compile(r'>\s?\[!(NOTE|IMPORTANT|WARNING|TIP)\]\s*\n((?:>.*(?:\n|$))*)', re.IGNORECASE)
    md_text = alert_pattern.sub(replace_alerts, md_text)

    # Pre-process Mermaid blocks: ```mermaid ... ```
    def replace_mermaid(match):
        diagram_code = match.group(1).strip()
        return f'<div class="mermaid">\n{diagram_code}\n</div>'

    mermaid_pattern = re.compile(r'```mermaid\s*\n(.*?)```', re.DOTALL)
    md_text = mermaid_pattern.sub(replace_mermaid, md_text)

    # Convert Markdown to HTML
    html_body = markdown.markdown(
        md_text,
        extensions=['extra', 'tables', 'fenced_code', 'toc', 'sane_lists']
    )

    # Full HTML Document with KaTeX, Mermaid, and Print CSS
    full_html = f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>{title}</title>
    
    <!-- KaTeX for Math Equations -->
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/katex.min.css">
    <script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/katex.min.js"></script>
    <script defer src="https://cdn.jsdelivr.net/npm/katex@0.16.8/dist/contrib/auto-render.min.js"
        onload="renderMathInElement(document.body, {{delimiters: [
            {{left: '$$', right: '$$', display: true}},
            {{left: '$', right: '$', display: false}},
            {{left: '\\\\[', right: '\\\\]', display: true}},
            {{left: '\\\\(', right: '\\\\)', display: false}}
        ]}});"></script>

    <!-- Mermaid.js for Diagrams -->
    <script src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
    <script>
        mermaid.initialize({{
            startOnLoad: true,
            theme: 'neutral',
            flowchart: {{ useMaxWidth: true, htmlLabels: true, curve: 'basis' }}
        }});
    </script>

    <style>
        @page {{
            size: A4;
            margin: 1.8cm 1.4cm 1.8cm 1.4cm;
            @bottom-right {{
                content: counter(page);
                font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
                font-size: 8pt;
                color: #718096;
            }}
        }}

        body {{
            font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, 'Helvetica Neue', Arial, sans-serif;
            color: #2D3748;
            line-height: 1.55;
            font-size: 9.5pt;
            background-color: #FFFFFF;
            margin: 0;
            padding: 0;
        }}

        h1 {{
            color: #1A365D;
            font-size: 18pt;
            font-weight: 700;
            border-bottom: 2.5px solid #2B6CB0;
            padding-bottom: 5pt;
            margin-top: 0;
            margin-bottom: 12pt;
            page-break-after: avoid;
        }}

        h2 {{
            color: #2B6CB0;
            font-size: 13pt;
            font-weight: 600;
            border-bottom: 1px solid #E2E8F0;
            padding-bottom: 3pt;
            margin-top: 16pt;
            margin-bottom: 8pt;
            page-break-after: avoid;
        }}

        h3 {{
            color: #2D3748;
            font-size: 10.5pt;
            font-weight: 600;
            margin-top: 12pt;
            margin-bottom: 4pt;
            page-break-after: avoid;
        }}

        p, ul, ol {{
            margin-top: 0;
            margin-bottom: 8pt;
        }}

        li {{
            margin-bottom: 3pt;
        }}

        hr {{
            border: 0;
            height: 1px;
            background: #E2E8F0;
            margin: 14pt 0;
        }}

        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 10pt 0 14pt 0;
            font-size: 8.5pt;
            page-break-inside: avoid;
        }}

        th {{
            background-color: #1A365D;
            color: #FFFFFF;
            font-weight: 600;
            text-align: left;
            padding: 6pt 8pt;
            border: 1px solid #2D3748;
        }}

        td {{
            padding: 5pt 8pt;
            border: 1px solid #E2E8F0;
            vertical-align: top;
        }}

        tr:nth-child(even) td {{
            background-color: #F8FAFC;
        }}

        .alert {{
            border-left: 4px solid #3182CE;
            background-color: #EBF8FF;
            padding: 8pt 12pt;
            margin: 10pt 0;
            border-radius: 0 4px 4px 0;
            page-break-inside: avoid;
        }}

        .alert-important {{
            border-left-color: #DD6B20;
            background-color: #FFFAF0;
        }}

        .alert p {{
            margin-bottom: 0;
        }}

        img {{
            max-width: 95%;
            height: auto;
            display: block;
            margin: 12pt auto;
            border-radius: 4px;
            border: 1px solid #E2E8F0;
            box-shadow: 0 2px 6px rgba(0, 0, 0, 0.08);
            page-break-inside: avoid;
        }}

        .mermaid {{
            text-align: center;
            margin: 12pt 0;
            page-break-inside: avoid;
        }}

        code {{
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 8.5pt;
            background-color: #EDF2F7;
            padding: 1pt 3pt;
            border-radius: 3pt;
            color: #C53030;
        }}

        pre {{
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 8pt;
            background-color: #F7FAFC;
            border: 1px solid #E2E8F0;
            border-radius: 4px;
            padding: 8pt;
            overflow-x: auto;
            margin: 8pt 0;
            page-break-inside: avoid;
        }}

        pre code {{
            background: none;
            padding: 0;
            color: #2D3748;
        }}

        blockquote {{
            border-left: 3px solid #CBD5E0;
            padding-left: 10pt;
            margin-left: 0;
            color: #4A5568;
            font-style: italic;
        }}

        @media print {{
            body {{
                font-size: 9pt;
            }}
            .no-print {{
                display: none;
            }}
        }}
    </style>
</head>
<body>
{html_body}
</body>
</html>
"""
    return full_html

def convert_md_file_to_pdf(md_path, output_pdf_path, title):
    if not os.path.exists(md_path):
        print(f"Error: {md_path} not found.")
        return False

    with open(md_path, 'r', encoding='utf-8') as f:
        md_text = f.read()

    html_content = render_markdown_to_html(md_text, title)

    temp_html_path = output_pdf_path.replace('.pdf', '_temp.html')
    with open(temp_html_path, 'w', encoding='utf-8') as f:
        f.write(html_content)

    print(f"HTML written to {temp_html_path}")
    print(f"Generating PDF via Microsoft Edge: {output_pdf_path}...")

    cmd = [
        EDGE_PATH,
        "--headless",
        "--disable-gpu",
        "--virtual-time-budget=6000",
        "--run-all-compositor-stages-before-draw",
        "--no-pdf-header-footer",
        f"--print-to-pdf={output_pdf_path}",
        temp_html_path
    ]

    res = subprocess.run(cmd, capture_output=True, text=True)
    if res.returncode != 0:
        print(f"Error running Edge: {res.stderr}")
        return False

    if os.path.exists(output_pdf_path) and os.path.getsize(output_pdf_path) > 0:
        print(f"SUCCESS: Generated {output_pdf_path} ({os.path.getsize(output_pdf_path):,} bytes)")
        if os.path.exists(temp_html_path):
            os.remove(temp_html_path)
        return True
    else:
        print(f"Failed to generate valid PDF for {output_pdf_path}")
        return False

def main():
    brain_dir = r"C:\Users\armge\.gemini\antigravity\brain\a81a308a-ce3a-4739-91e0-57f14d4face7"
    docs_dir = r"C:\Coding\ARVR Project\Documents"
    os.makedirs(docs_dir, exist_ok=True)

    files_to_convert = [
        {
            "md": os.path.join(brain_dir, "Judges_Project_Technical_Overview.md"),
            "pdf_targets": [
                os.path.join(docs_dir, "Group15_Technical_Dossier_Judges_Presentation.pdf"),
                os.path.join(brain_dir, "Judges_Project_Technical_Overview.pdf")
            ],
            "title": "Group 15 - Acrophobia VR Exposure Technical Dossier"
        },
        {
            "md": os.path.join(brain_dir, "Student_Internal_Viva_CheatSheet.md"),
            "pdf_targets": [
                os.path.join(docs_dir, "Group15_Student_Internal_Viva_CheatSheet.pdf"),
                os.path.join(brain_dir, "Student_Internal_Viva_CheatSheet.pdf")
            ],
            "title": "Group 15 - Viva Voce Defense Cheat-Sheet & Guide"
        }
    ]

    for item in files_to_convert:
        md_file = item["md"]
        for target_pdf in item["pdf_targets"]:
            convert_md_file_to_pdf(md_file, target_pdf, item["title"])

if __name__ == "__main__":
    main()

