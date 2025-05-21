from paddleocr import PaddleOCR
import sys
import json

def run_ocr(image_path):
    ocr = PaddleOCR(use_angle_cls=True, lang='en')
    result = ocr.ocr(image_path, cls=True)

    lines = []
    for line in result:
        for word_info in line:
            text = word_info[1][0]
            lines.append(text)
    return '\n'.join(lines)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(json.dumps({"error": "No image path provided"}))
        sys.exit(1)

    path = sys.argv[1]
    try:
        extracted_text = run_ocr(path)
        print(json.dumps({"text": extracted_text}))
    except Exception as e:
        print(json.dumps({"error": str(e)}))
        sys.exit(1)
