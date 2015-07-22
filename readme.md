# Semantic Syntax Colorizer

A Visual Studio 2015 editor extension for semantic syntax highlighting.

It uses the Roslyn APIs to highlight the following syntax types in distinctive colors to make them easily recognizable.

* Class fields
* Enum fields
* Static methods
* Regular methods
* Constructors
* Type parameters
* Parameters
* Namespaces
* Class properties
* Local variables
* Special types (built in)
* Normal types

The code is fairly simple, but it is not currently written using the Async APIs in Roslyn.

This extension works for the first final version of Visual Studio 2015.
