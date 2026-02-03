# OPS Viewer

**Visualize and analyze flight operations and Areas of Responsibility (AoRs) with an intuitive and interactive map-based interface.**

This application provides a powerful solution for uploading, visualizing, and analyzing flight operation plans (OPS) and Areas of Responsibility (AoRs). It allows users to load data from JSON files, view geofenced areas on a map, filter operations by timeframe, and inspect the details of each operation or AoR. The interactive interface and detailed data visualization capabilities make it an essential tool for air traffic controllers, drone operators, and aviation authorities.

## Features

- **Interactive Map Visualization**: Utilizes MapLibre GL to render flight operation volumes and AoRs as interactive polygons on a map, providing a clear and intuitive visual representation of airspace usage.
- **Data Upload and Parsing**: Supports uploading data from JSON files for both flight operations (OPS) and Areas of Responsibility (AoRs). The application is designed to handle various data formats by normalizing the data into a consistent internal format.
- **Timeframe Filtering**: Allows users to filter flight operations by a selected date range, making it easy to focus on specific timeframes and declutter the map view.
- **Detailed Information Display**: Provides a detailed breakdown of each flight operation and AoR, including metadata, location, altitude, and timeframe. The details are presented in a clear and organized manner, making it easy to access the information you need.
- **Multi-Select and Area Calculation**: Supports selecting multiple operations or AoRs and calculates the total area of the selected zones, providing valuable insights into airspace usage.
- **Data Export**: Allows users to export selected flight operations or AoRs to JSON or XLSX format for further analysis or reporting.
- **Responsive Design**: The application is designed to be responsive and work seamlessly on different screen sizes, ensuring a consistent user experience across devices.

## Getting Started

To get started with the OPS Viewer, follow these simple steps to set it up and run it on your local machine:

1.  **Clone the repository**:

    ```sh
    git clone <YOUR_GIT_URL>
    ```

2.  **Navigate to the project directory**:

    ```sh
    cd <YOUR_PROJECT_NAME>
    ```

3.  **Install the dependencies**:

    ```sh
    npm install
    ```

4.  **Run the development server**:

    ```sh
    npm run dev
    ```

This will start the development server and open the application in your default browser. You can now start uploading and visualizing your flight operations and AoRs.

## Usage

- **Uploading Data**: Use the file upload functionality to load your OPS or AoR data from a JSON file. The application will automatically parse and display the data on the map.
- **Filtering Operations**: Use the date range picker to filter flight operations by a specific timeframe. The map will update to show only the operations that fall within the selected range.
- **Inspecting Details**: Click on any operation or AoR on the map or in the list to view its detailed information. The details panel will provide a comprehensive breakdown of the selected item.
- **Exporting Data**: Select the operations or AoRs you want to export and use the export functionality to save the data as a JSON or XLSX file.

## Technology Stack

- **[Vite](https://vitejs.dev/)**: A next-generation front-end build tool that provides a faster and leaner development experience.
- **[React](https://reactjs.org/)**: A popular JavaScript library for building user interfaces.
- **[TypeScript](https://www.typescriptlang.org/)**: A statically typed superset of JavaScript that adds type safety and improves code quality.
- **[MapLibre GL](https://maplibre.org/)**: An open-source library for creating interactive maps from vector tiles and styles.
- **[Tailwind CSS](https://tailwindcss.com/)**: A utility-first CSS framework for rapidly building custom designs.
- **[shadcn/ui](https://ui.shadcn.com/)**: A collection of beautifully designed, accessible, and customizable UI components.
- **[Turf.js](https://turfjs.org/)**: A JavaScript library for spatial analysis, used for calculations like area and bounding boxes.

## Contributing

Contributions are welcome! If you have any ideas, suggestions, or bug reports, please open an issue on the GitHub repository. If you would like to contribute code, please fork the repository and submit a pull request.
