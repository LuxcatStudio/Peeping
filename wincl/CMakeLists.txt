cmake_minimum_required(VERSION 3.15)
project(ActiveWindowMonitor)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(MSVC)
    add_compile_options(/W4)
    add_compile_options(/W4 /utf-8)
endif()

add_executable(ActiveWindowMonitor MonitorService.cpp)

target_link_libraries(ActiveWindowMonitor PRIVATE
    winhttp
)