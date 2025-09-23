#pragma once
#include <regex>
#include <unordered_map>
#include <glm/vec2.hpp>
#include <glm/vec4.hpp>

#include "imgui.h"


void parsePosition(const std::string& input, glm::vec2& ratio, glm::vec2& pixel);
std::vector<std::string> split(const std::string& str, char delimiter);
bool wildcardMatch(std::string text, std::string pattern);
bool regexMatch(std::string text, std::string pattern);

// 优化的正则表达式匹配器类
class RegexMatcher {
private:
    static std::unordered_map<std::string, std::shared_ptr<std::regex>> regex_cache;
    static const size_t MAX_CACHE_SIZE;
    
public:
    // 预编译正则表达式并缓存
    static std::shared_ptr<std::regex> getCompiledRegex(const std::string& pattern);
    
    // 优化的匹配函数
    static bool match(const std::string& text, const std::string& pattern);
    
    // 清理缓存（可选，用于内存管理）
    static void clearCache();
};

glm::vec4 convertToVec4(uint32_t value);
ImVec4 convertToImVec4(uint32_t value);
bool caseInsensitiveStrStr(const char* haystack, const char* needle);