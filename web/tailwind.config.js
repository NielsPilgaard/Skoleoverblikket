/** @type {import('tailwindcss').Config} */
export default {
  content: [
    './index.html',
    './src/**/*.{js,ts,jsx,tsx}',
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Lato', 'sans-serif'],
        display: ['"Playfair Display"', 'Georgia', 'serif'],
      },
      colors: {
        brand: {
          50:  '#f0f5f1',
          100: '#d9ead9',
          200: '#b0d4b0',
          300: '#7db87d',
          400: '#4e9a4e',
          500: '#2d7d2d',
          600: '#1f6321',
          700: '#174d19',
          800: '#113b14',
          900: '#0c2c0e',
        },
      },
    },
  },
  plugins: [],
}
