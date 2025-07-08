#include <string>
#include <sstream>
#include <glm/vec2.hpp>
#include <regex>
#include <glm/vec4.hpp>

#include "imgui.h"

// Function to parse the input string and convert to ratio and pixel vectors
void parsePosition(const std::string &input, glm::vec2 &ratio, glm::vec2 &pixel) {
    std::regex re(R"(([-+]?\d*\.?\d+)(%|px))");
    std::smatch match;
    std::string::const_iterator searchStart(input.cbegin());

    ratio = glm::vec2(0.0f);
    pixel = glm::vec2(0.0f);
    int index = 0;

    while (std::regex_search(searchStart, input.cend(), match, re) && index < 2) {
        float value = std::stof(match[1].str());
        std::string unit = match[2].str();

        if (unit == "%") {
            ratio[index] += value / 100.0f;
        } else if (unit == "px") {
            pixel[index] += value;
        }

        searchStart = match.suffix().first;

        if (searchStart != input.cend() && (*searchStart == ',' || *searchStart == ' ')) {
            ++index;
            ++searchStart;
        }
    }
}


std::vector<std::string> split(const std::string &str, char delimiter) {
    std::vector<std::string> tokens;
    std::string token;
    for (char ch : str) {
        if (ch == delimiter) {
            if (!token.empty()) {
                tokens.push_back(token);
                token.clear();
            }
        } else {
            token += ch;
        }
    }
    if (!token.empty()) {
        tokens.push_back(token);
    }
    return tokens;
}

bool wildcardMatch(std::string text, std::string pattern)
{
    int n = text.length();
    int m = pattern.length();
    int i = 0, j = 0, startIndex = -1, match = 0;

    while (i < n) {
        if (j < m
            && (pattern[j] == '?'
                || pattern[j] == text[i])) {
            // Characters match or '?' in pattern matches
            // any character.
            i++;
            j++;
        }
        else if (j < m && pattern[j] == '*') {
            // Wildcard character '*', mark the current
            // position in the pattern and the text as a
            // proper match.
            startIndex = j;
            match = i;
            j++;
        }
        else if (startIndex != -1) {
            // No match, but a previous wildcard was found.
            // Backtrack to the last '*' character position
            // and try for a different match.
            j = startIndex + 1;
            match++;
            i = match;
        }
        else {
            // If none of the above cases comply, the
            // pattern does not match.
            return false;
        }
    }

    // Consume any remaining '*' characters in the given
    // pattern.
    while (j < m && pattern[j] == '*') {
        j++;
    }

    // If we have reached the end of both the pattern and
    // the text, the pattern matches the text.
    return j == m;
}

// RR GG BB AA
glm::vec4 convertToVec4(uint32_t value) {
	// Extract 8-bit channels from the 32-bit integer
	float r = (value) & 0xFF;
	float g = (value >> 8) & 0xFF;
	float b = (value >> 16) & 0xFF;
	float a = (value >> 24) & 0xFF;

	// Normalize the channels to [0.0, 1.0]
	return glm::vec4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
}

ImVec4 convertToImVec4(uint32_t value) {
    // Extract 8-bit channels from the 32-bit integer
    float a = (value >> 24) & 0xFF;
    float b = (value >> 16) & 0xFF;
    float g = (value >> 8) & 0xFF;
    float r = value & 0xFF;

    // Normalize the channels to [0.0, 1.0]
    return ImVec4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
}

bool caseInsensitiveStrStr(const char* haystack, const char* needle) {
    for (const char* h = haystack; *h != '\0'; ++h) {
        const char* hStart = h;
        const char* n = needle;

        while (*n != '\0' && *h != '\0' && tolower(*h) == tolower(*n)) {
            ++h;
            ++n;
        }

        if (*n == '\0') {
            return true; // Found
        }

        h = hStart; // Reset h to the start for the next iteration
    }
    return false; // Not found
}