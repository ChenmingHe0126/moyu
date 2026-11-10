import React from 'react';

/** The cut is made twice: Chinese (Intro) and English (IntroEn). Every visible line is written as t(zh, en). */
export type Lang = 'zh' | 'en';
export const LangContext = React.createContext<Lang>('zh');
export const useLang = () => React.useContext(LangContext);
export const useT = () => {
  const lang = useLang();
  return (zh: string, en: string) => (lang === 'en' ? en : zh);
};
