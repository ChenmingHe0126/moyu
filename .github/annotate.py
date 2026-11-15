"""Posts a test step's output as GitHub annotations.

    python3 .github/annotate.py <log file> <title> <exit status>

Annotations can be read by anyone through the API and on the run's summary page, while job logs need admin rights, so
the output is published this way: the tail of the log as one annotation (an error when the step failed, else a notice)
and the lines that report a problem as a second one.
"""
import sys

MARKERS = ("[FAIL]", "MISSING", "NOT FOUND", "OLDER THAN", "FAILED", "Unhandled exception", "Exception:", "error CS")


def escape(text):
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def main():
    path, title, status = sys.argv[1], sys.argv[2], int(sys.argv[3])
    with open(path, encoding="utf-8", errors="replace") as f:
        lines = f.read().splitlines()
    level = "error" if status else "notice"
    # GitHub keeps only the first few KB of an annotation, so post the end of the log, where the verdict is.
    tail = "\n".join(lines[-300:])[-3500:]
    print(f"::{level} title={title}::{escape(tail)}")
    problems = [line for line in lines if any(m in line for m in MARKERS)]
    if problems:
        print(f"::warning title={title}: problems::{escape(chr(10).join(problems[:60])[:3500])}")


if __name__ == "__main__":
    main()
