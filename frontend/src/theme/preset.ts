import { definePreset } from '@primeuix/themes'
import Aura from '@primeuix/themes/aura'

// primary -> sky, surface -> slate; same palette as assets/theme.css so both layers flip together.
export const struoPresetConfig = {
  semantic: {
    primary: {
      50: '{sky.50}', 100: '{sky.100}', 200: '{sky.200}', 300: '{sky.300}',
      400: '{sky.400}', 500: '{sky.500}', 600: '{sky.600}', 700: '{sky.700}',
      800: '{sky.800}', 900: '{sky.900}', 950: '{sky.950}',
    },
    colorScheme: {
      light: {
        primary: { color: '{primary.600}', hoverColor: '{primary.700}', activeColor: '{primary.800}' },
        surface: {
          0: '#ffffff', 50: '{slate.50}', 100: '{slate.100}', 200: '{slate.200}',
          300: '{slate.300}', 400: '{slate.400}', 500: '{slate.500}', 600: '{slate.600}',
          700: '{slate.700}', 800: '{slate.800}', 900: '{slate.900}', 950: '{slate.950}',
        },
      },
      dark: {
        primary: { color: '{primary.400}', hoverColor: '{primary.300}', activeColor: '{primary.200}' },
        surface: {
          0: '#ffffff', 50: '{slate.50}', 100: '{slate.100}', 200: '{slate.200}',
          300: '{slate.300}', 400: '{slate.400}', 500: '{slate.500}', 600: '{slate.600}',
          700: '{slate.700}', 800: '{slate.800}', 900: '{slate.900}', 950: '{slate.950}',
        },
      },
    },
  },
} as const

export const StruoPreset = definePreset(Aura, struoPresetConfig)
