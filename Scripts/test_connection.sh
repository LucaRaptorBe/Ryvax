#!/bin/bash
# test_connection.sh - Test la connexion au serveur Ryvax

SERVER_IP="${1:-}"
SERVER_PORT="${2:-7777}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

if [ -z "$SERVER_IP" ]; then
    echo "Usage: $0 <SERVER_IP> [SERVER_PORT]"
    echo "Example: $0 192.168.1.100 7777"
    exit 1
fi

echo "Testing connection to $SERVER_IP:$SERVER_PORT..."
echo ""

# Test 1: Ping
echo -n "1. Ping test: "
if ping -c 1 -W 2 "$SERVER_IP" &> /dev/null; then
    echo -e "${GREEN}✓ Success${NC}"
else
    echo -e "${RED}✗ Failed${NC} - Server unreachable"
fi

# Test 2: UDP port (requires netcat)
echo -n "2. UDP port $SERVER_PORT: "
if command -v nc &> /dev/null; then
    if nc -zvu "$SERVER_IP" "$SERVER_PORT" 2>&1 | grep -q "open\|succeeded"; then
        echo -e "${GREEN}✓ Open${NC}"
    else
        echo -e "${RED}✗ Closed or filtered${NC}"
    fi
else
    echo -e "${YELLOW}⚠ netcat not installed, skipping${NC}"
fi

# Test 3: SSH (pour vérifier que le serveur est accessible)
echo -n "3. SSH access: "
if ssh -o ConnectTimeout=2 -o BatchMode=yes root@"$SERVER_IP" exit 2>/dev/null; then
    echo -e "${GREEN}✓ Success${NC}"
else
    echo -e "${YELLOW}⚠ No SSH access (might need password/key)${NC}"
fi

# Test 4: Vérifier si le service tourne (si SSH accessible)
echo -n "4. Server service status: "
if ssh -o ConnectTimeout=2 -o BatchMode=yes root@"$SERVER_IP" "systemctl is-active ryvaxserver" 2>/dev/null | grep -q "active"; then
    echo -e "${GREEN}✓ Running${NC}"
else
    echo -e "${YELLOW}⚠ Cannot verify (SSH required)${NC}"
fi

echo ""
echo "Connection test complete."
echo "If all tests pass, try connecting from your Unity client with:"
echo "  Server Address: $SERVER_IP"
echo "  Server Port: $SERVER_PORT"
