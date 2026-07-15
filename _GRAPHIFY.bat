$env:OLLAMA_BASE_URL="http://localhost:11434/v1"
$env:OLLAMA_API_KEY="ollama"
$env:OLLAMA_MODEL="qwen25coder14b:latest"

graphify extract . --force --backend ollama