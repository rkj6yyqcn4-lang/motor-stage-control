#ifndef HKDAQ_MATLAB_H
#define HKDAQ_MATLAB_H

#ifdef __cplusplus
extern "C" {
#endif

/* MATLAB loadlibrary 专用简化头文件 */

/* 厂家头文件里用了 MYAPI，但没有定义，这里直接去掉 */
#ifndef MYAPI
#define MYAPI
#endif

/* 64位 MATLAB 下 __stdcall 可以简化 */
#ifdef _WIN64
#define STDCALL
#else
#define STDCALL __stdcall
#endif

/* 设备操作 */
MYAPI int STDCALL OpenUsbV12(void);
MYAPI int STDCALL CloseUsbV12(void);
MYAPI int STDCALL ResetUsbV12(void);
MYAPI int STDCALL ResetUsbPipeV12(void);

/* PWM 输出 */
MYAPI int STDCALL PWMOutSetV12(int chan, int Freq, float DutyCycle);

/* 数字输出 */
MYAPI int STDCALL DoSetV12(unsigned char chan, unsigned char state);

/* 数字输入，可选 */
MYAPI int STDCALL DiReadV12(unsigned char *value);

/* 读设备 ID，可选 */
MYAPI unsigned int STDCALL GetCardIdV12(void);

#ifdef __cplusplus
}
#endif

#endif