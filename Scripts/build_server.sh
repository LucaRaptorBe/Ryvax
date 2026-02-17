#!/bin/bash
# build_server.sh - Automatise la création d'un build serveur depuis Unity

set -e

# Configuration
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity}"
PROJECT_PATH="$(pwd)"
BUILD_PATH="/Users/syris/Dev/LinuxServer"
BUILD_NAME="LinuxServer"
LOG_FILE="./build_server.log"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Trouver Unity
UNITY_EXEC=$(ls -t $UNITY_PATH 2>/dev/null | head -n1)
if [ -z "$UNITY_EXEC" ] || [ ! -f "$UNITY_EXEC" ]; then
    print_error "Unity not found at: $UNITY_PATH"
    print_error "Set UNITY_PATH environment variable to your Unity installation"
    print_error "Example: export UNITY_PATH=/Applications/Unity/Hub/Editor/2022.3.0f1/Unity.app/Contents/MacOS/Unity"
    exit 1
fi

print_info "Found Unity at: $UNITY_EXEC"
print_info "Project: $PROJECT_PATH"
print_info "Build output: $BUILD_PATH"

# Créer le dossier de build
mkdir -p "$BUILD_PATH"

# Build Unity
print_info "Building Linux server (this may take several minutes)..."
"$UNITY_EXEC" \
    -quit \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -buildLinux64Player "$BUILD_PATH/$BUILD_NAME.x86_64" \
    -logFile "$LOG_FILE" \
    || {
        print_error "Build failed! Check log: $LOG_FILE"
        tail -n 50 "$LOG_FILE"
        exit 1
    }

print_info "✅ Build completed successfully!"
print_info "Build location: $BUILD_PATH"
print_info "Build size: $(du -sh "$BUILD_PATH" | cut -f1)"
print_info ""
print_info "Next steps:"
echo "  1. Test locally: cd $BUILD_PATH && ./$BUILD_NAME.x86_64 -batchmode -nographics -server"
echo "  2. Deploy to VPS: ./Scripts/deploy_server.sh <SERVER_IP> ryvaxserver $BUILD_PATH"
