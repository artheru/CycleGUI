#pragma once


void parsePosition(const std::string& input, glm::vec2& ratio, glm::vec2& pixel);
std::vector<std::string> split(const std::string& str, char delimiter);
bool wildcardMatch(std::string text, std::string pattern);
glm::vec4 convertToVec4(uint32_t value);
ImVec4 convertToImVec4(uint32_t value);
bool caseInsensitiveStrStr(const char* haystack, const char* needle);