# Resrcify.SharedKernel

# Description
Implemented a Clean Architecture Shared Kernel, including the building blocks for Domain Driven Design and the Result pattern, to be used in internal projects.
The different modules are seperated into different NuGet packages using pipelines and published on the public package manager for .NET.

# Table of Contents
## Modules
- [Caching](src/Resrcify.SharedKernel.Caching/)
- [Domain-Driven Design](src/Resrcify.SharedKernel.DomainDrivenDesign/)
- [Messaging](src/Resrcify.SharedKernel.Messaging/)
- [Result Framework](src/Resrcify.SharedKernel.ResultFramework/)
- [Repository](src/Resrcify.SharedKernel.Repository/)
- [Unit Of Work](src/Resrcify.SharedKernel.UnitOfWork/)
- [Web](src/Resrcify.SharedKernel.Web/)
## Samples
- [Web Api Example](samples/Resrcify.SharedKernel.WebApiExample/)

## Contributions
First off, thank you for considering contributing to this project. We appreciate any contributions, from reporting issues to writing code, improving documentation, and suggesting new features.

### Contribution Process

1. **Fork the repository**: Click the "Fork" button at the top of the repository page.
2. **Clone your fork**:
    ```bash
    git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
    ```
3. **Create a branch** for your changes:
    ```bash
    git checkout -b feature/your-feature-name
    ```
4. **Make your changes**: Ensure your code follows the project’s coding style by implementing / using the .editorconfig as provided in this repository.
5. **Write tests** (if applicable) to ensure the functionality works as expected.
6. **Commit your changes**: Write clear and concise commit messages.
    ```bash
    git commit -m "Add feature or fix bug"
    ```
7. **Push your branch**:
    ```bash
    git push origin feature/your-feature-name
    ```
8. **Create a pull request**: Submit a PR to the `master` branch on the original repository. In your PR description, explain the changes you’ve made and reference any related issues.

# Credits
Inspired by [Milan Jovanovic](https://www.youtube.com/@MilanJovanovicTech)'s Clean Architecture series to create this Shared Kernel.