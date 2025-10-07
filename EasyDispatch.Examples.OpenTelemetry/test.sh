#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

API_URL="${API_URL:-http://localhost:5000}"

echo -e "${BLUE}============================================${NC}"
echo -e "${BLUE}EasyDispatch OpenTelemetry Demo - Test Suite${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""

# Check if API is running
echo -e "${YELLOW}Checking if API is running...${NC}"
if ! curl -s -f "${API_URL}/health" > /dev/null; then
    echo -e "${RED}API is not running at ${API_URL}${NC}"
    echo -e "${YELLOW}Start the API with: dotnet run${NC}"
    exit 1
fi
echo -e "${GREEN}API is running${NC}"
echo ""

# Test 1: Get User (Query)
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}1 Test: Get User (Query)${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Expected Activity: ${GREEN}EasyDispatch.Query.GetUserQuery${NC}"
echo ""
curl -s "${API_URL}/users/5" | jq '.'
echo ""
sleep 1

# Test 2: Create User (Command + Notifications)
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}2 Test: Create User (Command + Notifications)${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Expected Activities:"
echo -e "  - ${GREEN}EasyDispatch.Command.CreateUserCommand${NC}"
echo -e "  - ${GREEN}EasyDispatch.Notification.UserCreatedNotification${NC}"
echo -e "    3 handlers (Email, Analytics, Cache)"
echo ""
curl -s -X POST "${API_URL}/users" \
  -H "Content-Type: application/json" \
  -d '{"name":"John Doe","email":"john@example.com"}' | jq '.'
echo ""
sleep 1

# Test 3: Get User Orders - Success
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}3 Test: Get User Orders - Success${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Expected Activity: ${GREEN}EasyDispatch.Query.GetUserOrdersQuery${NC}"
echo -e "Status: ${GREEN}Ok${NC}"
echo ""
curl -s "${API_URL}/users/5/orders" | jq '.'
echo ""
sleep 1

# Test 4: Get User Orders - Error
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}4 Test: Get User Orders - Error (Demo)${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Expected Activity: ${GREEN}EasyDispatch.Query.GetUserOrdersQuery${NC}"
echo -e "Status: ${RED}Error${NC}"
echo -e "Exception: User 15 not found"
echo ""
curl -s "${API_URL}/users/15/orders"
echo ""
sleep 1

# Test 5: Delete User (Void Command)
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}5 Test: Delete User (Void Command)${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Expected Activity: ${GREEN}EasyDispatch.Command.DeleteUserCommand${NC}"
echo ""
curl -s -X DELETE "${API_URL}/users/999" -w "HTTP Status: %{http_code}\n"
echo ""
sleep 1

# Test 6: Multiple Rapid Requests (Load Test)
echo -e "${BLUE}============================================${NC}"
echo -e "${YELLOW}6 Test: Multiple Rapid Requests${NC}"
echo -e "${BLUE}============================================${NC}"
echo -e "Sending 10 rapid requests to test tracing under load..."
echo ""
for i in {1..10}; do
    curl -s "${API_URL}/users/$i" > /dev/null &
done
wait
echo -e "${GREEN}Completed 10 concurrent requests${NC}"
echo ""

# Summary
echo -e "${BLUE}============================================${NC}"
echo -e "${GREEN}Test Suite Complete!${NC}"
echo -e "${BLUE}============================================${NC}"
echo ""
echo -e "${YELLOW}Check the traces in:${NC}"
echo "  Console output (if using Console exporter)"
echo "  Jaeger UI at http://localhost:16686 (if using Jaeger)"
echo ""
echo -e "${YELLOW}What to look for:${NC}"
echo "  Activity names: EasyDispatch.Query/Command/Notification.*"
echo "  Parent-child relationships (HTTP -> Mediator)"
echo "  Tags: operation, message_type, handler_type, etc."
echo "  Error status on failed requests"
echo "  Multiple handlers for notifications"
echo ""