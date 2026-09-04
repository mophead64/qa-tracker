/** @type {import('tailwindcss').Config} */
module.exports = {
  darkMode: "class",
  content: [
    "./Components/**/*.{razor,html,cshtml}",
    // Presentation helpers that emit class names from C#.
    "./Projects/**/*.cs",
    "./TestCases/**/*.cs",
    "./Defects/**/*.cs",
    "./wwwroot/index.html",
  ],
  theme: {
    extend: {
      colors: {
        // QA Tracker brand green.
        brand: {
          50: "#ecfdf3",
          100: "#d1fadf",
          200: "#a6f4c5",
          300: "#6ce9a6",
          400: "#32d583",
          500: "#12b76a",
          600: "#039855",
          700: "#027a48",
          800: "#05603a",
          900: "#054f31",
          950: "#032b1c",
        },
      },
    },
  },
  plugins: [],
};
