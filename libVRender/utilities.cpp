#include <string>
#include <sstream>
#include <glm/vec2.hpp>
#include <regex>

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